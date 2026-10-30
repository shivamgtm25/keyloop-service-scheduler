using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceScheduler.Api.Application;
using ServiceScheduler.Api.Data;
using ServiceScheduler.Api.Domain;

namespace ServiceScheduler.Api.Api;

[ApiController]
[Route("api/appointments")]
[Produces("application/json")]
public class AppointmentsController(BookingService booking, SchedulerDbContext db) : ControllerBase
{
    /// <summary>Books an appointment, allocating a free bay and a qualified technician.</summary>
    /// <remarks>Send an <c>Idempotency-Key</c> header to make retries safe.</remarks>
    [HttpPost]
    [ProducesResponseType<AppointmentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<AppointmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        CreateAppointmentRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var outcome = await booking.BookAsync(new BookingRequest(
            request.CustomerId, request.VehicleId, request.DealershipId, request.ServiceTypeId,
            request.StartTime!.Value, idempotencyKey), ct);

        var body = AppointmentResponse.From(outcome.Appointment);
        return outcome.Created
            ? CreatedAtAction(nameof(Get), new { id = body.Id }, body)
            : Ok(body);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType<AppointmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var appointment = await db.Appointments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct)
                          ?? throw SchedulingException.NotFound("Appointment", id);
        return Ok(AppointmentResponse.From(appointment));
    }

    /// <summary>Lists appointments, optionally filtered by dealership and/or customer.</summary>
    [HttpGet]
    public async Task<IEnumerable<AppointmentResponse>> List(
        [FromQuery] int? dealershipId, [FromQuery] int? customerId, CancellationToken ct)
    {
        IQueryable<Appointment> query = db.Appointments.AsNoTracking();
        if (dealershipId is not null) query = query.Where(a => a.DealershipId == dealershipId);
        if (customerId is not null) query = query.Where(a => a.CustomerId == customerId);

        var rows = await query.OrderBy(a => a.StartUtc).Take(500).ToListAsync(ct);
        return rows.Select(AppointmentResponse.From);
    }

    /// <summary>Cancels an appointment, releasing its bay and technician. Idempotent.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        await booking.CancelAsync(id, ct);
        return NoContent();
    }
}
