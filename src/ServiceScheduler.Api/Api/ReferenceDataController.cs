using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceScheduler.Api.Application;
using ServiceScheduler.Api.Data;

namespace ServiceScheduler.Api.Api;

[ApiController]
[Route("api")]
[Produces("application/json")]
public class ReferenceDataController(SchedulerDbContext db, AvailabilityService availability) : ControllerBase
{
    [HttpGet("dealerships")]
    public async Task<IActionResult> Dealerships(CancellationToken ct) =>
        Ok(await db.Dealerships.AsNoTracking()
            .Select(d => new
            {
                d.Id, d.Name, d.TimeZoneId, d.OpensAt, d.ClosesAt,
                Bays = d.Bays.Select(b => new { b.Id, b.Name }),
                Technicians = d.Technicians.Select(t => new
                {
                    t.Id, t.Name, QualifiedServiceTypeIds = t.Qualifications.Select(q => q.ServiceTypeId),
                }),
            })
            .ToListAsync(ct));

    [HttpGet("service-types")]
    public async Task<IActionResult> ServiceTypes(CancellationToken ct) =>
        Ok(await db.ServiceTypes.AsNoTracking().ToListAsync(ct));

    [HttpGet("customers")]
    public async Task<IActionResult> Customers(CancellationToken ct) =>
        Ok(await db.Customers.AsNoTracking().Include(c => c.Vehicles).ToListAsync(ct));

    /// <summary>Bookable start times (UTC) for a service at a dealership on a local date.</summary>
    [HttpGet("dealerships/{dealershipId:int}/availability")]
    [ProducesResponseType<IReadOnlyList<AvailableSlot>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Availability(
        int dealershipId, [FromQuery] int serviceTypeId, [FromQuery] DateOnly date, CancellationToken ct) =>
        Ok(await availability.GetSlotsAsync(dealershipId, serviceTypeId, date, ct));
}
