using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ServiceScheduler.Api.Api;
using ServiceScheduler.Api.Data;
using ServiceScheduler.Api.Domain;

namespace ServiceScheduler.Tests;

/// <summary>Spins up the real HTTP pipeline against a throwaway SQLite file.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"scheduler-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Scheduler", $"Data Source={_dbPath};Pooling=False");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(Seed.Now));
        });
    }

    public int AddVehicle()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulerDbContext>();
        var vehicle = new Vehicle
        {
            CustomerId = Seed.Alice, Vin = Guid.NewGuid().ToString("N")[..17].ToUpperInvariant(),
            Make = "Kia", Model = "Ceed", Year = 2022,
        };
        db.Vehicles.Add(vehicle);
        db.SaveChanges();
        return vehicle.Id;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        File.Delete(_dbPath);
    }
}

public class ApiTests : IAsyncLifetime
{
    private readonly ApiFactory _factory = new();
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static object BookingBody(int vehicleId = Seed.AliceCar, int serviceType = Seed.OilChange,
        DateTimeOffset? start = null) => new
    {
        customerId = Seed.Alice, vehicleId, dealershipId = Seed.London, serviceTypeId = serviceType,
        startTime = start ?? Seed.Monday(9),
    };

    [Fact]
    public async Task Post_creates_appointment_and_returns_location_that_can_be_fetched()
    {
        var response = await _client.PostAsJsonAsync("/api/appointments", BookingBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<AppointmentResponse>();
        Assert.Equal("Confirmed", created!.Status);

        var fetched = await _client.GetFromJsonAsync<AppointmentResponse>(response.Headers.Location);
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal(created.TechnicianId, fetched.TechnicianId);
        Assert.Equal(created.ServiceBayId, fetched.ServiceBayId);
    }

    [Fact]
    public async Task Malformed_request_is_rejected_with_400()
    {
        var response = await _client.PostAsJsonAsync("/api/appointments", new { customerId = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Conflict_returns_409_problem_details_with_reason_code()
    {
        await _client.PostAsJsonAsync("/api/appointments", BookingBody(serviceType: Seed.FullService));

        var response = await _client.PostAsJsonAsync("/api/appointments",
            BookingBody(_factory.AddVehicle(), Seed.FullService, Seed.Monday(10)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("no_technician_available", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Retried_post_with_same_idempotency_key_returns_original()
    {
        async Task<HttpResponseMessage> Send()
        {
            using var msg = new HttpRequestMessage(HttpMethod.Post, "/api/appointments")
            {
                Content = JsonContent.Create(BookingBody()),
            };
            msg.Headers.Add("Idempotency-Key", "abc-123");
            return await _client.SendAsync(msg);
        }

        var first = await Send();
        var second = await Send();

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal((await first.Content.ReadFromJsonAsync<AppointmentResponse>())!.Id,
                     (await second.Content.ReadFromJsonAsync<AppointmentResponse>())!.Id);
    }

    [Fact]
    public async Task Concurrent_retries_with_same_idempotency_key_all_return_the_original()
    {
        // A client timing out and retrying while the first request is still in flight.
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
        {
            var msg = new HttpRequestMessage(HttpMethod.Post, "/api/appointments")
            {
                Content = JsonContent.Create(BookingBody()),
            };
            msg.Headers.Add("Idempotency-Key", "retry-storm");
            return _client.SendAsync(msg);
        }));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(4, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        var ids = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<AppointmentResponse>()));
        Assert.Single(ids.Select(a => a!.Id).Distinct());
    }

    [Fact]
    public async Task Concurrent_requests_for_the_same_slot_never_double_book_resources()
    {
        // London has 3 bays / 3 oil-qualified techs: exactly 3 of 10 simultaneous requests may succeed.
        var vehicles = Enumerable.Range(0, 10).Select(_ => _factory.AddVehicle()).ToList();

        var responses = await Task.WhenAll(vehicles.Select(v =>
            _client.PostAsJsonAsync("/api/appointments", BookingBody(v))));

        var created = responses.Where(r => r.StatusCode == HttpStatusCode.Created).ToList();
        Assert.Equal(3, created.Count);
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var booked = await Task.WhenAll(created.Select(r => r.Content.ReadFromJsonAsync<AppointmentResponse>()));
        Assert.Equal(3, booked.Select(b => b!.ServiceBayId).Distinct().Count());
        Assert.Equal(3, booked.Select(b => b!.TechnicianId).Distinct().Count());
    }

    [Fact]
    public async Task Cancel_returns_204_and_marks_appointment_cancelled()
    {
        var created = await (await _client.PostAsJsonAsync("/api/appointments", BookingBody()))
            .Content.ReadFromJsonAsync<AppointmentResponse>();

        var response = await _client.DeleteAsync($"/api/appointments/{created!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var fetched = await _client.GetFromJsonAsync<AppointmentResponse>($"/api/appointments/{created.Id}");
        Assert.Equal("Cancelled", fetched!.Status);
    }

    [Fact]
    public async Task Availability_endpoint_returns_bookable_slots()
    {
        var slots = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/dealerships/{Seed.London}/availability?serviceTypeId={Seed.FullService}&date=2026-11-02");

        Assert.Equal(15, slots.GetArrayLength());
    }

    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
