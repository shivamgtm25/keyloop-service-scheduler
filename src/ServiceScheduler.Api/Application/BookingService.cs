using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using ServiceScheduler.Api.Data;
using ServiceScheduler.Api.Domain;

namespace ServiceScheduler.Api.Application;

public record BookingRequest(
    int CustomerId, int VehicleId, int DealershipId, int ServiceTypeId,
    DateTimeOffset StartTime, string? IdempotencyKey = null);

public record BookingOutcome(Appointment Appointment, bool Created);

public class BookingService(
    SchedulerDbContext db,
    DealershipLock dealershipLock,
    SchedulerMetrics metrics,
    TimeProvider clock,
    ILogger<BookingService> logger)
{
    public async Task<BookingOutcome> BookAsync(BookingRequest request, CancellationToken ct = default)
    {
        using var activity = SchedulerMetrics.ActivitySource.StartActivity("BookAppointment");
        activity?.SetTag("dealership.id", request.DealershipId);
        var sw = Stopwatch.StartNew();
        try
        {
            var outcome = await BookCoreAsync(request, ct);
            if (outcome.Created) metrics.Booked(request.DealershipId);
            return outcome;
        }
        catch (SchedulingException ex)
        {
            metrics.Rejected(ex.Code);
            logger.LogInformation("Booking rejected: {Reason} (dealership {DealershipId}, vehicle {VehicleId})",
                ex.Code, request.DealershipId, request.VehicleId);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Code);
            throw;
        }
        finally
        {
            metrics.RecordDuration(sw.Elapsed);
        }
    }

    private async Task<BookingOutcome> BookCoreAsync(BookingRequest request, CancellationToken ct)
    {
        // Replayed request: return the original booking instead of creating a duplicate.
        if (await FindReplayAsync(request.IdempotencyKey, ct) is { } replay) return replay;

        var dealership = await db.Dealerships.AsNoTracking().FirstOrDefaultAsync(d => d.Id == request.DealershipId, ct)
                         ?? throw SchedulingException.NotFound("Dealership", request.DealershipId);
        var serviceType = await db.ServiceTypes.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.ServiceTypeId, ct)
                          ?? throw SchedulingException.NotFound("Service type", request.ServiceTypeId);
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct)
                       ?? throw SchedulingException.NotFound("Customer", request.CustomerId);
        var vehicle = await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == request.VehicleId, ct)
                      ?? throw SchedulingException.NotFound("Vehicle", request.VehicleId);

        if (vehicle.CustomerId != customer.Id)
            throw SchedulingException.Invalid("vehicle_not_owned", "The vehicle does not belong to this customer.");

        var startUtc = request.StartTime.UtcDateTime;
        var endUtc = startUtc.AddMinutes(serviceType.DurationMinutes);

        if (startUtc <= clock.GetUtcNow().UtcDateTime)
            throw SchedulingException.Invalid("start_in_past", "The requested start time must be in the future.");

        if (!BusinessHours.IsWithinOpeningHours(dealership, startUtc, endUtc))
            throw SchedulingException.Invalid("outside_opening_hours",
                $"The {serviceType.DurationMinutes}-minute service must fit within opening hours " +
                $"({dealership.OpensAt:HH\\:mm}-{dealership.ClosesAt:HH\\:mm} {dealership.TimeZoneId}).");

        // Critical section: availability check and insert must be atomic per dealership.
        using var _ = await dealershipLock.AcquireAsync(dealership.Id, ct);

        // A concurrent retry may have committed while we waited for the lock.
        if (await FindReplayAsync(request.IdempotencyKey, ct) is { } inFlightReplay) return inFlightReplay;

        var vehicleBusy = await db.Appointments.AnyAsync(a =>
            a.VehicleId == vehicle.Id && a.Status == AppointmentStatus.Confirmed
            && a.StartUtc < endUtc && a.EndUtc > startUtc, ct);
        if (vehicleBusy)
            throw SchedulingException.Conflict("vehicle_already_booked", "This vehicle already has an appointment in that period.");

        var bays = await db.ServiceBays.AsNoTracking().Where(b => b.DealershipId == dealership.Id).ToListAsync(ct);
        var technicians = await db.Technicians.AsNoTracking().Include(t => t.Qualifications)
            .Where(t => t.DealershipId == dealership.Id).ToListAsync(ct);
        var overlapping = await db.Appointments.AsNoTracking()
            .Where(a => a.DealershipId == dealership.Id && a.Status == AppointmentStatus.Confirmed
                        && a.StartUtc < endUtc && a.EndUtc > startUtc)
            .ToListAsync(ct);

        var allocation = AllocationEngine.Allocate(bays, technicians, overlapping, serviceType.Id, startUtc, endUtc);
        if (!allocation.Succeeded)
            throw SchedulingException.Conflict(ToCode(allocation.Failure), ToMessage(allocation.Failure));

        var appointment = new Appointment
        {
            CustomerId = customer.Id,
            VehicleId = vehicle.Id,
            DealershipId = dealership.Id,
            ServiceTypeId = serviceType.Id,
            TechnicianId = allocation.Technician!.Id,
            ServiceBayId = allocation.Bay!.Id,
            StartUtc = startUtc,
            EndUtc = endUtc,
            Status = AppointmentStatus.Confirmed,
            CreatedUtc = clock.GetUtcNow().UtcDateTime,
            IdempotencyKey = request.IdempotencyKey,
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Appointment {AppointmentId} confirmed: bay {BayId}, technician {TechnicianId}, {Start:o}",
            appointment.Id, appointment.ServiceBayId, appointment.TechnicianId, startUtc);
        return new BookingOutcome(appointment, Created: true);
    }

    public async Task CancelAsync(int appointmentId, CancellationToken ct = default)
    {
        var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId, ct)
                          ?? throw SchedulingException.NotFound("Appointment", appointmentId);
        if (appointment.Status == AppointmentStatus.Cancelled) return; // idempotent

        appointment.Status = AppointmentStatus.Cancelled;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Appointment {AppointmentId} cancelled", appointmentId);
    }

    private async Task<BookingOutcome?> FindReplayAsync(string? idempotencyKey, CancellationToken ct)
    {
        if (idempotencyKey is null) return null;
        var existing = await db.Appointments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.IdempotencyKey == idempotencyKey, ct);
        return existing is null ? null : new BookingOutcome(existing, Created: false);
    }

    private static string ToCode(AllocationFailure f) => f switch
    {
        AllocationFailure.NoBayAvailable => "no_bay_available",
        AllocationFailure.NoTechnicianAvailable => "no_technician_available",
        _ => "no_capacity",
    };

    private static string ToMessage(AllocationFailure f) => f switch
    {
        AllocationFailure.NoBayAvailable => "A qualified technician is free but every service bay is occupied for the requested period.",
        AllocationFailure.NoTechnicianAvailable => "A service bay is free but no qualified technician is available for the requested period.",
        _ => "Neither a service bay nor a qualified technician is available for the requested period.",
    };
}
