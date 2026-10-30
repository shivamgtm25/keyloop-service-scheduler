using Microsoft.EntityFrameworkCore;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;
using ServiceScheduler.Api.Application;
using ServiceScheduler.Api.Data;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON logs to stdout: the container platform ships them to the log store.
builder.Services.AddSerilog((services, lc) => lc
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter()));

builder.Services.AddDbContext<SchedulerDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Scheduler") ?? "Data Source=scheduler.db"));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DealershipLock>();
builder.Services.AddSingleton<SchedulerMetrics>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<AvailabilityService>();

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<SchedulingExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<SchedulerDbContext>("database");

// Traces + metrics via OpenTelemetry. Exported over OTLP when OTEL_EXPORTER_OTLP_ENDPOINT is set
// (e.g. to Jaeger / Grafana / Azure Monitor); otherwise collected in-process only.
var otel = builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("service-scheduler"))
    .WithTracing(t => t
        .AddSource(SchedulerMetrics.ActivitySource.Name)
        .AddAspNetCoreInstrumentation())
    .WithMetrics(m => m
        .AddMeter(SchedulerMetrics.Name)
        .AddAspNetCoreInstrumentation());
if (!string.IsNullOrEmpty(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
    otel.UseOtlpExporter();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SchedulerDbContext>();
    await db.Database.EnsureCreatedAsync();
    await SeedData.EnsureSeededAsync(db);
}

app.UseExceptionHandler();
app.UseSerilogRequestLogging();

app.MapOpenApi();
app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "Service Scheduler API"));

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program;
