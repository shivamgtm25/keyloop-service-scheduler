using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ServiceScheduler.Api.Application;

/// <summary>Business-level telemetry, exported through OpenTelemetry.</summary>
public sealed class SchedulerMetrics : IDisposable
{
    public const string Name = "ServiceScheduler";

    public static readonly ActivitySource ActivitySource = new(Name);

    private readonly Meter _meter = new(Name);
    private readonly Counter<long> _booked;
    private readonly Counter<long> _rejected;
    private readonly Histogram<double> _bookingDuration;

    public SchedulerMetrics()
    {
        _booked = _meter.CreateCounter<long>("appointments.booked", description: "Confirmed bookings");
        _rejected = _meter.CreateCounter<long>("appointments.rejected", description: "Rejected booking attempts by reason");
        _bookingDuration = _meter.CreateHistogram<double>("appointments.booking.duration", "ms", "Booking request latency");
    }

    public void Booked(int dealershipId) =>
        _booked.Add(1, new KeyValuePair<string, object?>("dealership.id", dealershipId));

    public void Rejected(string reason) =>
        _rejected.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void RecordDuration(TimeSpan elapsed) => _bookingDuration.Record(elapsed.TotalMilliseconds);

    public void Dispose() => _meter.Dispose();
}
