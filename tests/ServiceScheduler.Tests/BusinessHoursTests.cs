using ServiceScheduler.Api.Domain;

namespace ServiceScheduler.Tests;

public class BusinessHoursTests
{
    private static readonly Dealership London = new()
    {
        Name = "London", TimeZoneId = "Europe/London", OpensAt = new(8, 0), ClosesAt = new(17, 0),
    };

    private static DateTime Utc(int month, int day, int h, int m = 0) => new(2026, month, day, h, m, 0, DateTimeKind.Utc);

    [Fact]
    public void Accepts_a_window_inside_opening_hours() =>
        Assert.True(BusinessHours.IsWithinOpeningHours(London, Utc(11, 2, 8), Utc(11, 2, 17)));

    [Fact]
    public void Rejects_a_service_that_would_overrun_closing_time() =>
        Assert.False(BusinessHours.IsWithinOpeningHours(London, Utc(11, 2, 16), Utc(11, 2, 18)));

    [Fact]
    public void Rejects_a_start_before_opening() =>
        Assert.False(BusinessHours.IsWithinOpeningHours(London, Utc(11, 2, 7, 30), Utc(11, 2, 8, 0)));

    [Fact]
    public void Interprets_opening_hours_in_dealership_local_time_across_daylight_saving()
    {
        // July: London is UTC+1, so 08:00 local opening == 07:00 UTC.
        Assert.True(BusinessHours.IsWithinOpeningHours(London, Utc(7, 1, 7), Utc(7, 1, 8)));
        Assert.False(BusinessHours.IsWithinOpeningHours(London, Utc(7, 1, 6, 30), Utc(7, 1, 7, 30)));
        Assert.Equal(Utc(7, 1, 7), BusinessHours.ToUtc(London, new DateOnly(2026, 7, 1), new TimeOnly(8, 0)));
    }
}
