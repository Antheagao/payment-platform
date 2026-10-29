---
name: payment-platform-architecture
description: Payment Platform system shape, what each backend project holds today, where new use cases, endpoints, jobs, ports, and configuration go, and the decision records. Use when adding a feature, job, adapter, or setting, or crossing the dashboard/API/worker boundary in this repository.
---

# Payment Platform architecture overlay

Project facts only; the generic module, port, and ADR rules live in the global `design-principles` guide and its overlay.
`docs/architecture.md` and `docs/decisions/` come first.

## System shape

- The browser talks only to the Next.js dashboard; its server code calls the API with the merchant's JWT (ADR 004).
- The API (`http://localhost:5080`) owns every rule and writes each state change, its audit row, and its outbox message in one transaction.
- The worker (`http://localhost:5081`, health only) runs the asynchronous workflow as periodic jobs over SQL Server tables (ADR 003).
- SQL Server is the single source of truth, queue included; the acquirer is a port with a simulated adapter.

## Backend today

| Project | Key files |
| --- | --- |
| `Domain` | `Payments/Payment.cs` (the aggregate), `Payments/PaymentLifecycle.cs` (the transition table), `Monetary/Money.cs`, `Result.cs`, `Actor.cs` |
| `Persistence` | `PaymentsDbContext.cs`, `PersistenceServiceCollectionExtensions.cs` (`AddPersistence`), `DatabaseHealthCheck.cs`, `PaymentsDbContextFactory.cs` |
| `Application` | `ApplicationServiceCollectionExtensions.cs` (`AddApplication`), `Acquiring/` (`IAcquirer`, `SimulatedAcquirer`), `ApplicationTelemetry.cs` |
| `ServiceDefaults` | `ServiceDefaultsExtensions.cs` (`AddServiceDefaults`, `UseCorrelationIds`, `MapDefaultEndpoints`), `CorrelationIds.cs`, `HealthResponseWriter.cs` |
| `Api` | `Program.cs`, `Endpoints/ApiRoutes.cs`, `Errors/`, `SecurityHeadersMiddleware.cs` |
| `Worker` | `Program.cs`, `Jobs/PeriodicJob.cs` (the loop), `Jobs/WorkerTelemetry.cs`, `Jobs/JobsServiceCollectionExtensions.cs` |

No use case, endpoint resource, table, or job exists yet; the queue in `notes/future.md` (T19 onward) adds them.

## Where new work goes

- A use case: `Application/<Feature>/<VerbPhrase>.cs` with its input, result, and handler; registered in `AddApplication`; it reads the merchant id and correlation id from its input, never from an ambient `HttpContext`.
- An endpoint: `Api/Endpoints/<Resource>Endpoints.cs`, mapped onto the `/api/v1` group, calling exactly one use case.
- A job: `Worker/Jobs/<Name>Job.cs` deriving from `PeriodicJob`, registered with `AddHostedService` in `AddWorkerJobs`; it calls one use case per run and restores the correlation id of the rows it processes with `CorrelationIds.BeginScope`.
- An external system: a port and its adapters in `Application/<System>/`, chosen at startup; the simulated adapter stays for tests and local runs.
- A table: an entity configuration and a migration in `Persistence`, owned by one feature; other features change its rows only through that feature's use cases.

## Configuration

| Key | Read by | Notes |
| --- | --- | --- |
| `ConnectionStrings:Payments` | API, worker | Required at startup; environment or user secrets locally (compose connects as `payments_app`, never `sa`), Key Vault in Azure |
| `Database:MigrateOnStartup` | API only | `true` only in compose; deployments migrate in the pipeline |
| `Acquirer:Simulated:LatencyMilliseconds` | API, worker | 0 to 5000, validated at startup |
| `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_SERVICE_NAME` | API, worker | Export only when the endpoint is set; the service name defaults to the assembly name (compose sets `payment-api` and `payment-worker`) |
| `ASPNETCORE_ENVIRONMENT` | API, worker | `Development` enables OpenAPI and disables HSTS |

A new setting lands in one commit across its validated options class, `appsettings.json` (non-secret defaults only), `docker-compose.yml` (a local-only secret gets a compose default and a commented line in `.env.example`), this table, and the How it runs section of `notes/present.md`.

## Containers

- `backend/Dockerfile` builds both hosts from one restore (`--target api`, `--target worker`) in locked mode onto `aspnet:10.0-noble-chiseled-extra`: no shell, non-root, port 8080.
- `docker-compose.yml`: `sqlserver` (2025-CU10, healthchecked), `sqlserver-init` (one-shot `scripts/sql/create-local-database.sql`: the database and the `payments_app` login, db_owner of that database only), `aspire-dashboard` (13.6.2, anonymous, OTLP on 4317), `api`, `worker`, `frontend`; every port on 127.0.0.1 and movable through a `*_PORT` variable; no `.env` needed; SQL Server pinned to `linux/amd64` (emulated on Apple silicon).

## Decision records

- 001 stack and layout, 002 project boundaries, 003 transactional outbox, 004 JWT behind the dashboard's server, 005 OpenTelemetry, 006 problem details, 007 idempotency keys.
- Expected next: card tokenization and the vault (T20), webhook signing and endpoint registration (T23), the settlement procedure (T24).
