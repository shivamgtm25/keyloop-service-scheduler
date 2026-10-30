namespace ServiceScheduler.Api.Domain;

public enum AllocationFailure
{
    None,
    NoBayAvailable,
    NoTechnicianAvailable,
    NoBayOrTechnicianAvailable,
}

public readonly record struct AllocationResult(
    ServiceBay? Bay, Technician? Technician, AllocationFailure Failure)
{
    public bool Succeeded => Failure == AllocationFailure.None;
}

/// <summary>
/// Pure, side-effect-free allocation logic: given the resources of a dealership and the
/// appointments already on the books, decide whether a bay AND a qualified technician are
/// both free for the whole [start, end) window. No I/O so it is trivially unit-testable.
/// </summary>
public static class AllocationEngine
{
    /// <summary>Half-open interval overlap: back-to-back bookings do not conflict.</summary>
    public static bool Overlaps(DateTime aStart, DateTime aEnd, DateTime bStart, DateTime bEnd) =>
        aStart < bEnd && bStart < aEnd;

    public static AllocationResult Allocate(
        IEnumerable<ServiceBay> bays,
        IEnumerable<Technician> technicians,
        IEnumerable<Appointment> existing,
        int serviceTypeId,
        DateTime startUtc,
        DateTime endUtc)
    {
        var conflicting = existing
            .Where(a => a.Status == AppointmentStatus.Confirmed
                        && Overlaps(a.StartUtc, a.EndUtc, startUtc, endUtc))
            .ToList();

        var busyBays = conflicting.Select(a => a.ServiceBayId).ToHashSet();
        var busyTechs = conflicting.Select(a => a.TechnicianId).ToHashSet();

        // Deterministic choice (lowest id) keeps behaviour predictable and testable.
        var bay = bays.Where(b => !busyBays.Contains(b.Id)).OrderBy(b => b.Id).FirstOrDefault();
        var tech = technicians
            .Where(t => t.IsQualifiedFor(serviceTypeId) && !busyTechs.Contains(t.Id))
            .OrderBy(t => t.Id)
            .FirstOrDefault();

        return (bay, tech) switch
        {
            (not null, not null) => new(bay, tech, AllocationFailure.None),
            (null, not null) => new(null, null, AllocationFailure.NoBayAvailable),
            (not null, null) => new(null, null, AllocationFailure.NoTechnicianAvailable),
            _ => new(null, null, AllocationFailure.NoBayOrTechnicianAvailable),
        };
    }
}
