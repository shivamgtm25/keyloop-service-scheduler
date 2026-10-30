# Unified Service Scheduler (Keyloop Technical Assessment, Scenario A)

A backend REST API that books vehicle service appointments at dealerships. A booking is confirmed only when a **service bay** and a **technician qualified for that service** are both free for the **entire service duration**. The confirmed appointment is persisted, linking customer, vehicle, technician and bay.

- **Layer implemented:** backend (ASP.NET Core on .NET 10, EF Core, SQLite).
- **Client stub:** OpenAPI (`/openapi/v1.json`) with Swagger UI (`/swagger`), plus runnable requests in [ServiceScheduler.Api.http](src/ServiceScheduler.Api/ServiceScheduler.Api.http).
- **Design document:** [docs/SYSTEM_DESIGN.md](docs/SYSTEM_DESIGN.md).

## Repository layout

```
src/ServiceScheduler.Api/
  Domain/         Entities, AllocationEngine (pure availability logic), BusinessHours (time zones)
  Application/    BookingService, AvailabilityService, DealershipLock, metrics, error handling
  Data/           EF Core DbContext + seed data
  Api/            Controllers and request/response contracts
tests/ServiceScheduler.Tests/
  AllocationEngineTests, BusinessHoursTests     unit tests (pure logic)
  BookingServiceTests, AvailabilityServiceTests service tests against real SQLite
  ApiTests                                      HTTP integration tests incl. concurrency
docs/SYSTEM_DESIGN.md
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Optional: Visual Studio 2022 17.14+/2026, or VS Code with C# Dev Kit

No database server is needed. SQLite creates `scheduler.db` on first run and seeds demo data.

## Build, run, test

```bash
dotnet build
dotnet test
dotnet run --project src/ServiceScheduler.Api      # http://localhost:5076
```

Then open **http://localhost:5076/swagger**, or send the requests in `ServiceScheduler.Api.http`. In Visual Studio, open `ServiceScheduler.slnx`, set `ServiceScheduler.Api` as the startup project and press F5. Use **Test → Run All Tests** to run the suite.

To reset the data, stop the app and delete `src/ServiceScheduler.Api/scheduler.db`.

> **Windows Smart App Control:** if it's turned on, Windows blocks locally compiled, unsigned binaries ("An Application Control policy has blocked this file"). This applies to `dotnet run`, `dotnet test` and Visual Studio alike. Run on a machine or VM with Smart App Control off, or inside WSL, a Dev Container or GitHub Codespaces.

### Seed data

| Dealership | Bays | Technicians (qualified service type ids) |
|---|---|---|
| 1 London (Europe/London, 08:00–17:00) | 3 | Tech A (1,2) · Tech B (1,2,3) · Tech C (1,2,3,4) |
| 2 Manchester (same hours) | 3 | Tech A (1,2) · Tech B (1,2,3) · Tech C (1,2,3,4) |

Service types: 1 Oil Change 30 min · 2 MOT 60 · 3 Brake Service 90 · 4 Full Service 120.
Customers: 1 Alice (vehicle 1) · 2 Bob (vehicle 2).

## API

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/api/appointments` | Book. `201` + `Location` on success. Optional `Idempotency-Key` header; a replay returns `200` with the original. |
| `GET` | `/api/appointments/{id}` | Fetch one appointment |
| `GET` | `/api/appointments?dealershipId=&customerId=` | List appointments |
| `DELETE` | `/api/appointments/{id}` | Cancel (idempotent, frees the resources) → `204` |
| `GET` | `/api/dealerships/{id}/availability?serviceTypeId=&date=YYYY-MM-DD` | Bookable slots (UTC) for a local date |
| `GET` | `/api/dealerships`, `/api/service-types`, `/api/customers` | Reference data |
| `GET` | `/health` | Liveness/readiness (includes the DB check) |

```bash
curl -X POST http://localhost:5076/api/appointments \
  -H "Content-Type: application/json" -H "Idempotency-Key: demo-1" \
  -d '{"customerId":1,"vehicleId":1,"dealershipId":1,"serviceTypeId":4,"startTime":"2026-12-01T09:00:00Z"}'
```

Errors use RFC 7807 `application/problem+json` with a stable `code` and a `traceId`:

| Status | `code` | Meaning |
|---|---|---|
| 400 | (validation) | Malformed request |
| 404 | `not_found` | Unknown dealership, service type, customer, vehicle or appointment |
| 409 | `no_bay_available` / `no_technician_available` / `no_capacity` | Valid request, but no capacity at that time |
| 409 | `vehicle_already_booked` | The vehicle already has an overlapping appointment |
| 422 | `start_in_past` / `outside_opening_hours` / `vehicle_not_owned` | A business rule was violated |

## Tests

These tests cover the core business logic:

- **Overlap semantics:** half-open intervals; back-to-back bookings are allowed and partial overlaps are rejected.
- **Whole-duration check:** a slot that is free at its start time but collides later is rejected.
- **Qualifications:** an unqualified technician is never assigned, even when free.
- **Distinct failure reasons:** no bay, no technician, or no capacity.
- **Business rules:** start in the past, overrunning closing time, opening hours in local time across daylight-saving changes, and vehicle ownership.
- **Vehicle double-booking:** prevented across dealerships.
- **Cancellation and idempotency:** cancelling releases capacity; an idempotency-key replay returns the original appointment, including when retries arrive while the original request is still in flight.
- **Availability:** slots match booking decisions, and past slots are hidden.
- **Concurrency:** 10 simultaneous HTTP requests against 3 units of capacity produce exactly 3 bookings, each with a distinct bay and technician.

## Observability

- **Logs:** Serilog, structured JSON to stdout, with per-request logs and business events.
- **Traces and metrics:** OpenTelemetry, with a custom `BookAppointment` span and the `appointments.booked`, `appointments.rejected{reason}` and `appointments.booking.duration` metrics. Set `OTEL_EXPORTER_OTLP_ENDPOINT` (e.g. `http://localhost:4317` for Jaeger) to export them.
- Every error response includes a `traceId`.

See section 7 of the design document for details.

## Known limitations

- The double-booking guard is an in-process per-dealership lock, so it's correct for a single instance. For multiple replicas, move to PostgreSQL with exclusion constraints on `tstzrange`. The design is described in section 5 of the design document.
- The schema is created with `EnsureCreated`. Production would use EF Core migrations.
- There is no authentication or authorisation.

## AI Collaboration Narrative

**Tool:** Claude Code (an agentic AI coding assistant) inside VS Code.

**Strategy.** I treated the AI as a fast implementer working under my direction, not as the decision-maker:

1. **Requirements first, code second.** I gave it the full brief and had it pick out the ambiguities and produce explicit assumptions before any code was written. I chose Scenario A and the backend layer, and I switched the stack to .NET/C# when I decided that was what I wanted to show.
2. **Architecture by constraint.** I asked for the core rule ("bay AND qualified technician free for the *entire* duration") as a **pure function** (`AllocationEngine`) with no I/O, so it can be verified in isolation. I also required availability queries to reuse that engine rather than duplicate its logic.
3. **Risk-driven prompting.** I asked specifically about concurrent double-booking, idempotent retries and time zones/DST, the places where plausible-looking code is most often wrong.

**Verification and refinement.** I didn't accept generated code just because it compiled:

- **Supply-chain check:** the template's OpenAPI package pulled in a dependency with a known high-severity vulnerability (NU1903). I upgraded it and confirmed the warning was gone from a clean build.
- **Framework pitfalls caught in review:** `[property: Range]` on positional records compiles but throws at runtime in ASP.NET Core MVC, so the attributes were moved to the constructor parameters. Error responses were changed to real `application/problem+json`. SQLite drops `DateTimeKind`, so a converter was added, plus a test that asserts UTC round-tripping.
- **Tests as the specification:** each business rule has a named test. Boundary cases (back-to-back slots, overrunning closing time, DST) were chosen deliberately, because they're where AI-written code most often has subtle errors. Integration tests run the real HTTP pipeline against real SQLite rather than an in-memory fake, so SQL translation and constraints are tested too.
- **Concurrency proven, not assumed:** a test fires 10 parallel requests at 3 units of capacity and asserts there's no double-booking.
- **A green test isn't proof:** reviewing the booking flow turned up a race. Concurrent retries with the same `Idempotency-Key` could both miss the key lookup, which happened before the lock, so the retry got `409 vehicle_already_booked`. The first test, written over HTTP, *passed*, because the race window is only milliseconds wide. I didn't take that as evidence of safety. I wrote a deterministic test that holds the dealership lock while both retries enter, watched it fail, then fixed the code by re-checking the key inside the lock, and watched the test pass.
- **Honest limits:** where a shortcut was taken (the in-process lock, `EnsureCreated`), it's documented together with the production alternative, instead of being left implicit.
- **Environment issue diagnosed:** builds were blocked at runtime by Windows Smart App Control. The AI read the Code Integrity event log to confirm the cause, instead of guessing or changing the code.

**Ownership.** I reviewed every file, can explain each design decision (see the design document), and the test suite is what proves the behaviour, not the AI's word.
