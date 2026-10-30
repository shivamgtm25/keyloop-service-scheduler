namespace ServiceScheduler.Api.Domain;

public class Dealership
{
    public int Id { get; set; }
    public required string Name { get; set; }

    /// <summary>IANA or Windows time zone id used to interpret opening hours.</summary>
    public required string TimeZoneId { get; set; }
    public TimeOnly OpensAt { get; set; }
    public TimeOnly ClosesAt { get; set; }

    public List<ServiceBay> Bays { get; set; } = [];
    public List<Technician> Technicians { get; set; } = [];
}

public class ServiceBay
{
    public int Id { get; set; }
    public int DealershipId { get; set; }
    public required string Name { get; set; }
}

public class ServiceType
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int DurationMinutes { get; set; }
}

public class Technician
{
    public int Id { get; set; }
    public int DealershipId { get; set; }
    public required string Name { get; set; }
    public List<TechnicianQualification> Qualifications { get; set; } = [];

    public bool IsQualifiedFor(int serviceTypeId) =>
        Qualifications.Any(q => q.ServiceTypeId == serviceTypeId);
}

public class TechnicianQualification
{
    public int TechnicianId { get; set; }
    public int ServiceTypeId { get; set; }
}

public class Customer
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public List<Vehicle> Vehicles { get; set; } = [];
}

public class Vehicle
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public required string Vin { get; set; }
    public required string Make { get; set; }
    public required string Model { get; set; }
    public int Year { get; set; }
}

public enum AppointmentStatus
{
    Confirmed = 1,
    Cancelled = 2,
}

public class Appointment
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int VehicleId { get; set; }
    public int DealershipId { get; set; }
    public int ServiceTypeId { get; set; }
    public int TechnicianId { get; set; }
    public int ServiceBayId { get; set; }

    /// <summary>Always UTC. Half-open interval [StartUtc, EndUtc).</summary>
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }

    public AppointmentStatus Status { get; set; } = AppointmentStatus.Confirmed;
    public DateTime CreatedUtc { get; set; }

    /// <summary>Optional client-supplied key making POST retries safe.</summary>
    public string? IdempotencyKey { get; set; }

    public Customer? Customer { get; set; }
    public Vehicle? Vehicle { get; set; }
    public Dealership? Dealership { get; set; }
    public ServiceType? ServiceType { get; set; }
    public Technician? Technician { get; set; }
    public ServiceBay? ServiceBay { get; set; }
}
