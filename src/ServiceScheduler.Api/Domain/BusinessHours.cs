namespace ServiceScheduler.Api.Domain;

public static class BusinessHours
{
    /// <summary>True when [startUtc, endUtc) falls within one local day's opening hours.</summary>
    public static bool IsWithinOpeningHours(Dealership dealership, DateTime startUtc, DateTime endUtc)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(dealership.TimeZoneId);
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(startUtc, tz);
        var localEnd = TimeZoneInfo.ConvertTimeFromUtc(endUtc, tz);

        return localStart.Date == localEnd.Date
               && TimeOnly.FromDateTime(localStart) >= dealership.OpensAt
               && TimeOnly.FromDateTime(localEnd) <= dealership.ClosesAt;
    }

    /// <summary>UTC instant for a local wall-clock time on the given local date.</summary>
    public static DateTime ToUtc(Dealership dealership, DateOnly localDate, TimeOnly localTime)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(dealership.TimeZoneId);
        var local = DateTime.SpecifyKind(localDate.ToDateTime(localTime), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, tz);
    }
}
