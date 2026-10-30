using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceScheduler.Api.Application;
using ServiceScheduler.Api.Data;
using ServiceScheduler.Api.Domain;

namespace ServiceScheduler.Tests;

public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Well-known values matching <see cref="SeedData"/>.</summary>
public static class Seed
{
    public const int London = 1, Manchester = 2;
    public const int OilChange = 1, Mot = 2, BrakeService = 3, FullService = 4;
    public const int Alice = 1, AliceCar = 1, Bob = 2, BobCar = 2;
    public const int LondonTechC = 3; // the only London technician qualified for Full Service

    // Sunday noon UTC; bookings are made for Monday. November => London is on GMT (UTC+0).
    public static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    public static DateTimeOffset Monday(int hour, int minute = 0) => new(2026, 11, 2, hour, minute, 0, TimeSpan.Zero);
}

/// <summary>
/// Real SQLite (in-memory, kept alive by an open connection) so tests exercise the actual
/// relational queries and constraints rather than the EF in-memory provider.
/// </summary>
public sealed class SqliteTestDb : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    public FixedTimeProvider Clock { get; } = new(Seed.Now);
    public DealershipLock Lock { get; } = new();

    public SqliteTestDb()
    {
        _connection.Open();
        using var db = NewContext();
        db.Database.EnsureCreated();
        SeedData.EnsureSeededAsync(db).GetAwaiter().GetResult();
    }

    public SchedulerDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SchedulerDbContext>().UseSqlite(_connection).Options);

    public BookingService NewBookingService(SchedulerDbContext db) =>
        new(db, Lock, new SchedulerMetrics(), Clock, NullLogger<BookingService>.Instance);

    /// <summary>Adds an extra vehicle for a customer, returning its id.</summary>
    public int AddVehicle(int customerId)
    {
        using var db = NewContext();
        var vehicle = new Vehicle
        {
            CustomerId = customerId, Vin = Guid.NewGuid().ToString("N")[..17].ToUpperInvariant(),
            Make = "Ford", Model = "Focus", Year = 2021,
        };
        db.Vehicles.Add(vehicle);
        db.SaveChanges();
        return vehicle.Id;
    }

    public void Dispose() => _connection.Dispose();
}
