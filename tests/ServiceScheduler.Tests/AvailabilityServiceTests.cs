using ServiceScheduler.Api.Application;

namespace ServiceScheduler.Tests;

public class AvailabilityServiceTests : IDisposable
{
    private readonly SqliteTestDb _db = new();
    private static readonly DateOnly Monday = new(2026, 11, 2);

    public void Dispose() => _db.Dispose();

    private async Task<IReadOnlyList<AvailableSlot>> Slots(int serviceType)
    {
        await using var ctx = _db.NewContext();
        return await new AvailabilityService(ctx, _db.Clock).GetSlotsAsync(Seed.London, serviceType, Monday);
    }

    [Fact]
    public async Task Empty_day_offers_every_slot_that_fits_before_closing()
    {
        var slots = await Slots(Seed.FullService);

        // 120-minute service, 08:00 open, 17:00 close, 30-min granularity: 08:00 .. 15:00 => 15 slots.
        Assert.Equal(15, slots.Count);
        Assert.Equal(Seed.Monday(8), slots[0].StartTime);
        Assert.Equal(Seed.Monday(15), slots[^1].StartTime);
        Assert.Equal(Seed.Monday(17), slots[^1].EndTime);
    }

    [Fact]
    public async Task Slots_that_would_overlap_an_existing_booking_are_withheld()
    {
        await using (var ctx = _db.NewContext())
            await _db.NewBookingService(ctx).BookAsync(new BookingRequest(
                Seed.Alice, Seed.AliceCar, Seed.London, Seed.FullService, Seed.Monday(9)));

        var starts = (await Slots(Seed.FullService)).Select(s => s.StartTime).ToList();

        // Tech C (only Full Service tech) is busy 09:00-11:00, so any 2h slot starting 08:00-10:30 clashes.
        Assert.DoesNotContain(Seed.Monday(8), starts);
        Assert.DoesNotContain(Seed.Monday(10, 30), starts);
        Assert.Equal(Seed.Monday(11), starts[0]);
    }

    [Fact]
    public async Task Slots_in_the_past_are_not_offered()
    {
        _db.Clock.Now = Seed.Monday(10, 10);

        var slots = await Slots(Seed.OilChange);

        Assert.Equal(Seed.Monday(10, 30), slots[0].StartTime);
    }
}
