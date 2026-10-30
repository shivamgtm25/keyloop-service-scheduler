using System.ComponentModel.DataAnnotations;
using ServiceScheduler.Api.Domain;

namespace ServiceScheduler.Api.Api;

// MVC validates record primary-constructor parameters, so attributes target the parameter.
public record CreateAppointmentRequest(
    [Range(1, int.MaxValue)] int CustomerId,
    [Range(1, int.MaxValue)] int VehicleId,
    [Range(1, int.MaxValue)] int DealershipId,
    [Range(1, int.MaxValue)] int ServiceTypeId,
    [Required] DateTimeOffset? StartTime);

public record AppointmentResponse(
    int Id,
    string Status,
    int CustomerId,
    int VehicleId,
    int DealershipId,
    int ServiceTypeId,
    int TechnicianId,
    int ServiceBayId,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    DateTimeOffset CreatedAt)
{
    public static AppointmentResponse From(Appointment a) => new(
        a.Id, a.Status.ToString(), a.CustomerId, a.VehicleId, a.DealershipId, a.ServiceTypeId,
        a.TechnicianId, a.ServiceBayId,
        new DateTimeOffset(a.StartUtc, TimeSpan.Zero),
        new DateTimeOffset(a.EndUtc, TimeSpan.Zero),
        new DateTimeOffset(a.CreatedUtc, TimeSpan.Zero));
}
