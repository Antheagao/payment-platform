# 002. Backend project boundaries

- Status: accepted
- Date: 2026-10-28

## Context

The backend has two hosts with different jobs: an HTTP API that merchants call, and a worker that runs the asynchronous workflow.
Both need the same payment rules and the same database, and the payment rules (the lifecycle above all) must be impossible to bypass.
The exercise grades clear boundaries, and a teammate should be able to find where a change goes by guessing.

## Decision

Six source projects, with dependencies pointing inward only:

| Project | Holds | May reference |
| --- | --- | --- |
| `PaymentPlatform.Domain` | The payment aggregate, the lifecycle transition table, `Money`, domain events, domain errors | The base class library only |
| `PaymentPlatform.Persistence` | `PaymentsDbContext`, entity configurations, migrations, T-SQL objects | Domain |
| `PaymentPlatform.Application` | Use cases in feature folders, ports for external systems with their adapters | Domain, Persistence |
| `PaymentPlatform.ServiceDefaults` | Logging, OpenTelemetry, health endpoints, the correlation id | Framework packages only |
| `PaymentPlatform.Api` | HTTP endpoints, authentication, problem details | Application, ServiceDefaults |
| `PaymentPlatform.Worker` | Background job hosting | Application, ServiceDefaults |

- **Feature folders, not layers, inside Application.** `Payments/CreatePayment.cs` holds the request, the handler, and its validation together; a feature that grows gets its own folder.
- **No repository over EF Core.** Use cases take `PaymentsDbContext` directly; `DbContext` is already a unit of work and `DbSet` a repository, and integration tests run against a real SQL Server, so a hand-written repository would be an abstraction with one implementation and no test double.
- **One port per external system,** with the adapter beside it and a fake for tests: the acquirer (`IAcquirer`, with the simulated adapter) and the webhook transport.
  The clock is `TimeProvider`, injected, so tests control time.
- **The lifecycle is enforced in the Domain.** `Payment` exposes methods (`Authorize`, `Capture`, `Settle`, `Refund`, `Fail`) and no public status setter, so no use case can make an illegal transition.
- **Hosts are thin.** An endpoint binds input, calls one use case, and maps the result to a response; a worker job calls one use case per run.
- Project references are enforced by architecture tests (`backend/tests/PaymentPlatform.ArchitectureTests`), not by convention.

## Rejected alternatives

- **Clean Architecture with repository and unit-of-work interfaces in Application and EF Core hidden in Infrastructure:** familiar, but it duplicates what EF Core already provides and doubles the files touched by every change.
- **A mediator library (MediatR) for dispatch:** it adds indirection and, since 2025, a commercial license; a handler class per use case, resolved from DI, is just as testable.
- **Use cases inside each host:** the worker's settlement and the API's refund share the same transition and audit code, so it belongs in one place both hosts reference.
- **A single project with folders:** simplest, but nothing would stop an endpoint from reaching into the worker's internals or the domain from taking a dependency on EF Core.

## Consequences

- Six projects is more than the smallest possible layout; each one is there because something must be kept out of it.
- Swapping the simulated acquirer for a real one touches one adapter and the DI registration.
- Persistence is a dependency of Application, so EF Core types appear in use cases; that is deliberate, and the Domain stays free of them.

## Amendments

- 2026-10-28: "request id" renamed to "correlation id"; see the amendment in [ADR 005](005-observability-on-opentelemetry.md).
