using Microsoft.EntityFrameworkCore;
using ServiceScheduler.Api.Domain;

namespace ServiceScheduler.Api.Data;

public static class SeedData
{
    public static async Task EnsureSeededAsync(SchedulerDbContext db)
    {
        if (await db.Dealerships.AnyAsync()) return;

        var oil = new ServiceType { Id = 1, Name = "Oil Change", DurationMinutes = 30 };
        var mot = new ServiceType { Id = 2, Name = "MOT Test", DurationMinutes = 60 };
        var brakes = new ServiceType { Id = 3, Name = "Brake Service", DurationMinutes = 90 };
        var full = new ServiceType { Id = 4, Name = "Full Service", DurationMinutes = 120 };
        db.ServiceTypes.AddRange(oil, mot, brakes, full);

        db.Dealerships.AddRange(
            BuildDealership(1, "Keyloop Motors London", 1, 1),
            BuildDealership(2, "Keyloop Motors Manchester", 4, 4));

        db.Customers.AddRange(
            new Customer
            {
                Id = 1, Name = "Alice Smith", Email = "alice@example.com",
                Vehicles = [new Vehicle { Id = 1, Vin = "1HGCM82633A004352", Make = "Honda", Model = "Accord", Year = 2020 }],
            },
            new Customer
            {
                Id = 2, Name = "Bob Jones", Email = "bob@example.com",
                Vehicles = [new Vehicle { Id = 2, Vin = "WVWZZZ1JZXW000001", Make = "Volkswagen", Model = "Golf", Year = 2018 }],
            });

        await db.SaveChangesAsync();
    }

    // 3 bays + 3 technicians per site, with deliberately different skill sets:
    //   tech 1: oil, MOT            tech 2: oil, MOT, brakes        tech 3: all services
    private static Dealership BuildDealership(int id, string name, int firstTechId, int firstBayId) => new()
    {
        Id = id,
        Name = name,
        TimeZoneId = "Europe/London",
        OpensAt = new TimeOnly(8, 0),
        ClosesAt = new TimeOnly(17, 0),
        Bays = Enumerable.Range(0, 3)
            .Select(i => new ServiceBay { Id = firstBayId + i, Name = $"Bay {i + 1}" }).ToList(),
        Technicians =
        [
            Tech(firstTechId, "Tech A", 1, 2),
            Tech(firstTechId + 1, "Tech B", 1, 2, 3),
            Tech(firstTechId + 2, "Tech C", 1, 2, 3, 4),
        ],
    };

    private static Technician Tech(int id, string name, params int[] serviceTypeIds) => new()
    {
        Id = id,
        Name = name,
        Qualifications = serviceTypeIds
            .Select(s => new TechnicianQualification { TechnicianId = id, ServiceTypeId = s }).ToList(),
    };
}
