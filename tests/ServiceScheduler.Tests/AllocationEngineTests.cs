using ServiceScheduler.Api.Domain;

namespace ServiceScheduler.Tests;

public class AllocationEngineTests
{
    private const int Oil = 1, Brakes = 3;
    private static readonly DateTime T9 = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);

    private static readonly List<ServiceBay> Bays = [new() { Id = 1, Name = "Bay 1" }, new() { Id = 2, Name = "Bay 2" }];

    private static readonly List<Technician> Techs =
    [
        Tech(10, Oil),
        Tech(20, Oil, Brakes),
    ];

    private static Technician Tech(int id, params int[] skills) => new()
    {
        Id = id, Name = $"T{id}",
        Qualifications = skills.Select(s => new TechnicianQualification { TechnicianId = id, ServiceTypeId = s }).ToList(),
    };

    private static Appointment Booked(int bay, int tech, DateTime start, int minutes,
        AppointmentStatus status = AppointmentStatus.Confirmed) => new()
    {
        ServiceBayId = bay, TechnicianId = tech, StartUtc = start, EndUtc = start.AddMinutes(minutes), Status = status,
    };

    [Theory]
    [InlineData(9, 0, 10, 0, 10, 0, 11, 0, false)] // back-to-back: end is exclusive
    [InlineData(9, 0, 10, 0, 8, 0, 9, 0, false)]   // back-to-back the other way
    [InlineData(9, 0, 10, 0, 9, 59, 10, 30, true)] // overlaps the last minute
    [InlineData(9, 0, 10, 0, 9, 15, 9, 45, true)]  // fully contained
    [InlineData(9, 0, 10, 0, 8, 0, 11, 0, true)]   // fully containing
    public void Overlaps_uses_half_open_intervals(int ah, int am, int aeh, int aem, int bh, int bm, int beh, int bem, bool expected)
    {
        DateTime At(int h, int m) => new(2026, 11, 2, h, m, 0, DateTimeKind.Utc);
        Assert.Equal(expected, AllocationEngine.Overlaps(At(ah, am), At(aeh, aem), At(bh, bm), At(beh, bem)));
    }

    [Fact]
    public void Allocates_lowest_free_bay_and_qualified_technician()
    {
        var result = AllocationEngine.Allocate(Bays, Techs, [], Oil, T9, T9.AddMinutes(30));

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Bay!.Id);
        Assert.Equal(10, result.Technician!.Id);
    }

    [Fact]
    public void Never_assigns_an_unqualified_technician_even_when_free()
    {
        // Tech 20 (the only brakes-qualified tech) is busy; tech 10 is free but unqualified.
        var existing = new[] { Booked(bay: 1, tech: 20, T9, 90) };

        var result = AllocationEngine.Allocate(Bays, Techs, existing, Brakes, T9.AddMinutes(30), T9.AddMinutes(120));

        Assert.False(result.Succeeded);
        Assert.Equal(AllocationFailure.NoTechnicianAvailable, result.Failure);
    }

    [Fact]
    public void Fails_with_no_bay_when_all_bays_occupied_but_technician_free()
    {
        var techs = Techs.Append(Tech(30, Oil)).ToList();
        var existing = new[] { Booked(1, 10, T9, 60), Booked(2, 20, T9, 60) };

        var result = AllocationEngine.Allocate(Bays, techs, existing, Oil, T9, T9.AddMinutes(30));

        Assert.Equal(AllocationFailure.NoBayAvailable, result.Failure);
    }

    [Fact]
    public void Checks_the_entire_service_duration_not_just_the_start()
    {
        // Free at 09:00 but a booking starts at 10:00; a 90-minute job would collide with it.
        var existing = new[] { Booked(1, 20, T9.AddHours(1), 60) };
        var singleBay = Bays.Take(1);

        var result = AllocationEngine.Allocate(singleBay, Techs, existing, Brakes, T9, T9.AddMinutes(90));

        Assert.False(result.Succeeded);
        Assert.Equal(AllocationFailure.NoBayOrTechnicianAvailable, result.Failure);
    }

    [Fact]
    public void Cancelled_appointments_do_not_block_resources()
    {
        var existing = new[] { Booked(1, 10, T9, 60, AppointmentStatus.Cancelled) };

        var result = AllocationEngine.Allocate(Bays, Techs, existing, Oil, T9, T9.AddMinutes(30));

        Assert.Equal(1, result.Bay!.Id);
        Assert.Equal(10, result.Technician!.Id);
    }
}
