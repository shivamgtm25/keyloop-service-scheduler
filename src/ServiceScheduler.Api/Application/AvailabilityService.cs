using Microsoft.EntityFrameworkCore;
using ServiceScheduler.Api.Data;
using ServiceScheduler.Api.Domain;

namespace ServiceScheduler.Api.Application;

public record AvailableSlot(DateTimeOffset StartTime, DateTimeOffset EndTime);

public class AvailabilityService(SchedulerDbContext db, TimeProvider clock)
{
    private const int SlotGranularityMinutes = 30;

    /// <summary>
    /// Bookable start times for one local day. Loads the day's data once and evaluates every slot
    /// in memory with the same <see cref="AllocationEngine"/> used at booking time, so what is
    /// advertised and what is accepted cannot drift apart.
    /// </summary>
    public async Task<IReadOnlyList<AvailableSlot>> GetSlotsAsync(
        int dealershipId, int serviceTypeId, DateOnly localDate, CancellationToken ct = default)
    {
        var dealership = await db.Dealerships.AsNoTracking().FirstOrDefaultAsync(d => d.Id == dealershipId, ct)
                         ?? throw SchedulingException.NotFound("Dealership", dealershipId);
        var serviceType = await db.ServiceTypes.AsNoTracking().FirstOrDefaultAsync(s => s.Id == serviceTypeId, ct)
                          ?? throw SchedulingException.NotFound("Service type", serviceTypeId);

        var dayStart = BusinessHours.ToUtc(dealership, localDate, dealership.OpensAt);
        var dayEnd = BusinessHours.ToUtc(dealership, localDate, dealership.ClosesAt);

        var bays = await db.ServiceBays.AsNoTracking().Where(b => b.DealershipId == dealershipId).ToListAsync(ct);
        var technicians = await db.Technicians.AsNoTracking().Include(t => t.Qualifications)
            .Where(t => t.DealershipId == dealershipId).ToListAsync(ct);
        var booked = await db.Appointments.AsNoTracking()
            .Where(a => a.DealershipId == dealershipId && a.Status == AppointmentStatus.Confirmed
                        && a.StartUtc < dayEnd && a.EndUtc > dayStart)
            .ToListAsync(ct);

        var now = clock.GetUtcNow().UtcDateTime;
        var duration = TimeSpan.FromMinutes(serviceType.DurationMinutes);
        var slots = new List<AvailableSlot>();

        for (var start = dayStart; start + duration <= dayEnd; start = start.AddMinutes(SlotGranularityMinutes))
        {
            if (start <= now) continue;
            var result = AllocationEngine.Allocate(bays, technicians, booked, serviceTypeId, start, start + duration);
            if (result.Succeeded)
                slots.Add(new AvailableSlot(new DateTimeOffset(start, TimeSpan.Zero), new DateTimeOffset(start + duration, TimeSpan.Zero)));
        }

        return slots;
    }
}
