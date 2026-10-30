using Microsoft.EntityFrameworkCore;
using ServiceScheduler.Api.Application;
using ServiceScheduler.Api.Domain;

namespace ServiceScheduler.Tests;

public class BookingServiceTests : IDisposable
{
    private readonly SqliteTestDb _db = new();

    public void Dispose() => _db.Dispose();

    private async Task<BookingOutcome> Book(int serviceType, DateTimeOffset start,
        int customer = Seed.Alice, int vehicle = Seed.AliceCar, int dealership = Seed.London, string? key = null)
    {
        await using var ctx = _db.NewContext();
        return await _db.NewBookingService(ctx).BookAsync(
            new BookingRequest(customer, vehicle, dealership, serviceType, start, key));
    }

    private async Task<SchedulingException> BookExpectingFailure(int serviceType, DateTimeOffset start,
        int customer = Seed.Alice, int vehicle = Seed.AliceCar, int dealership = Seed.London) =>
        await Assert.ThrowsAsync<SchedulingException>(() => Book(serviceType, start, customer, vehicle, dealership));

    [Fact]
    public async Task Confirmed_booking_is_persisted_with_customer_vehicle_technician_and_bay()
    {
        var outcome = await Book(Seed.OilChange, Seed.Monday(9));

        await using var ctx = _db.NewContext();
        var saved = await ctx.Appointments.SingleAsync(a => a.Id == outcome.Appointment.Id);
        Assert.True(outcome.Created);
        Assert.Equal(AppointmentStatus.Confirmed, saved.Status);
        Assert.Equal(Seed.Alice, saved.CustomerId);
        Assert.Equal(Seed.AliceCar, saved.VehicleId);
        Assert.Equal(Seed.Monday(9).UtcDateTime, saved.StartUtc);
        Assert.Equal(Seed.Monday(9, 30).UtcDateTime, saved.EndUtc); // oil change = 30 min
        Assert.Equal(DateTimeKind.Utc, saved.StartUtc.Kind);
        Assert.True(saved.TechnicianId > 0);
        Assert.True(saved.ServiceBayId > 0);
    }

    [Fact]
    public async Task Assigns_only_a_technician_qualified_for_the_service()
    {
        var outcome = await Book(Seed.FullService, Seed.Monday(9));

        Assert.Equal(Seed.LondonTechC, outcome.Appointment.TechnicianId);
    }

    [Fact]
    public async Task Rejects_when_the_only_qualified_technician_is_busy_even_though_bays_are_free()
    {
        await Book(Seed.FullService, Seed.Monday(9)); // 09:00-11:00 with Tech C

        var ex = await BookExpectingFailure(Seed.FullService, Seed.Monday(10), Seed.Bob, Seed.BobCar);

        Assert.Equal("no_technician_available", ex.Code);
        Assert.Equal(409, ex.Status);
    }

    [Fact]
    public async Task Back_to_back_bookings_on_the_same_resources_are_allowed()
    {
        await Book(Seed.FullService, Seed.Monday(9));                                      // 09:00-11:00
        var next = await Book(Seed.FullService, Seed.Monday(11), Seed.Bob, Seed.BobCar);   // 11:00-13:00

        Assert.Equal(Seed.LondonTechC, next.Appointment.TechnicianId);
    }

    [Fact]
    public async Task Rejects_when_dealership_capacity_is_exhausted()
    {
        // London has 3 bays and 3 oil-qualified technicians.
        for (var i = 0; i < 3; i++)
            await Book(Seed.OilChange, Seed.Monday(9), vehicle: _db.AddVehicle(Seed.Alice));

        var ex = await BookExpectingFailure(Seed.OilChange, Seed.Monday(9), vehicle: _db.AddVehicle(Seed.Alice));

        Assert.Equal("no_capacity", ex.Code);
    }

    [Fact]
    public async Task Capacity_at_one_dealership_does_not_affect_another()
    {
        for (var i = 0; i < 3; i++)
            await Book(Seed.OilChange, Seed.Monday(9), vehicle: _db.AddVehicle(Seed.Alice));

        var manchester = await Book(Seed.OilChange, Seed.Monday(9), vehicle: _db.AddVehicle(Seed.Alice),
            dealership: Seed.Manchester);

        Assert.Equal(Seed.Manchester, manchester.Appointment.DealershipId);
    }

    [Fact]
    public async Task A_vehicle_cannot_be_in_two_places_at_once()
    {
        await Book(Seed.OilChange, Seed.Monday(9));

        var ex = await BookExpectingFailure(Seed.Mot, Seed.Monday(9, 15), dealership: Seed.Manchester);

        Assert.Equal("vehicle_already_booked", ex.Code);
    }

    [Fact]
    public async Task Rejects_a_vehicle_that_belongs_to_another_customer()
    {
        var ex = await BookExpectingFailure(Seed.OilChange, Seed.Monday(9), Seed.Alice, Seed.BobCar);

        Assert.Equal("vehicle_not_owned", ex.Code);
        Assert.Equal(422, ex.Status);
    }

    [Fact]
    public async Task Rejects_start_times_in_the_past()
    {
        var ex = await BookExpectingFailure(Seed.OilChange, Seed.Now.AddHours(-1));

        Assert.Equal("start_in_past", ex.Code);
    }

    [Fact]
    public async Task Rejects_a_service_that_would_run_past_closing_time()
    {
        // Full service is 120 min; 16:00 + 2h = 18:00 > 17:00 close.
        var ex = await BookExpectingFailure(Seed.FullService, Seed.Monday(16));

        Assert.Equal("outside_opening_hours", ex.Code);
    }

    [Theory]
    [InlineData(999, Seed.OilChange)]
    [InlineData(Seed.London, 999)]
    public async Task Unknown_dealership_or_service_type_is_not_found(int dealership, int serviceType)
    {
        var ex = await BookExpectingFailure(serviceType, Seed.Monday(9), dealership: dealership);

        Assert.Equal(404, ex.Status);
    }

    [Fact]
    public async Task Cancelling_releases_the_bay_and_technician()
    {
        var first = await Book(Seed.FullService, Seed.Monday(9));
        await using (var ctx = _db.NewContext())
            await _db.NewBookingService(ctx).CancelAsync(first.Appointment.Id);

        var rebooked = await Book(Seed.FullService, Seed.Monday(9), Seed.Bob, Seed.BobCar);

        Assert.Equal(Seed.LondonTechC, rebooked.Appointment.TechnicianId);
    }

    [Fact]
    public async Task Replaying_an_idempotency_key_returns_the_original_booking()
    {
        var first = await Book(Seed.OilChange, Seed.Monday(9), key: "req-123");
        var replay = await Book(Seed.OilChange, Seed.Monday(9), key: "req-123");

        await using var ctx = _db.NewContext();
        Assert.False(replay.Created);
        Assert.Equal(first.Appointment.Id, replay.Appointment.Id);
        Assert.Equal(1, await ctx.Appointments.CountAsync());
    }

    [Fact]
    public async Task Idempotency_key_is_rechecked_after_waiting_for_the_dealership_lock()
    {
        // Hold the lock so both retries pass the early key lookup before either has inserted.
        var held = await _db.Lock.AcquireAsync(Seed.London, CancellationToken.None);
        var first = Book(Seed.OilChange, Seed.Monday(9), key: "in-flight");
        var retry = Book(Seed.OilChange, Seed.Monday(9), key: "in-flight");
        held.Dispose();

        var outcomes = await Task.WhenAll(first, retry);

        Assert.Single(outcomes, o => o.Created);
        Assert.Equal(outcomes[0].Appointment.Id, outcomes[1].Appointment.Id);
    }
}
