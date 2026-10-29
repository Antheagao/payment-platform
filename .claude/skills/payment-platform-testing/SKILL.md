---
name: payment-platform-testing
description: Payment Platform test harness and test projects - xUnit v3 on Microsoft.Testing.Platform, Testcontainers SQL Server with a database per test class, WebApplicationFactory over the real Program, fake clocks, metric and log collectors, and the known gaps. Use when writing, placing, or debugging a test in this repository.
---

# Payment Platform testing overlay

Load the global `testing` guide for the rules; this file holds what exists here and how the Rust mechanics translate.
The gate commands live in `AGENTS.md`; `dotnet test` from `backend/` runs everything and needs Docker running.

## Projects

| Project | Kind | Covers |
| --- | --- | --- |
| `PaymentPlatform.Domain.Tests` | Unit | Every status pair, aggregate transitions and events, money and currency boundaries |
| `PaymentPlatform.ServiceDefaults.Tests` | Unit | Correlation id rules and middleware, header injection, health output that leaks nothing |
| `PaymentPlatform.Application.Tests` | Unit | Every simulated acquirer branch, repeat calls, latency on a fake clock, options validation |
| `PaymentPlatform.Worker.Tests` | Unit | `PeriodicJob` schedule, failure isolation and counting, scope per run, clean stop |
| `PaymentPlatform.ArchitectureTests` | Rules | ADR 002's reference table (declared in each `.csproj` and used in IL), no web types in the core, hosts never use each other, concrete classes sealed, no public status setter |
| `PaymentPlatform.IntegrationTests` | Integration | The real API pipeline on SQL Server: probes against live and dead databases, problem details for 404 and 405, correlation ids, security headers |

## Frontend (`frontend/`)

| Kind | Where | Runs with |
| --- | --- | --- |
| Unit and component | `src/**/*.test.ts(x)`, beside the code | `npm test` (Vitest 5, jsdom, Testing Library, jest-dom matchers) |
| Browser end to end | `e2e/*.spec.ts` | `npm run e2e` (Playwright, Chromium, `desktop` 1440x900 and `mobile` 360x780 projects) |

- `vitest.config.mts` resolves the `@/*` alias natively and maps `server-only` to `test/empty-module.ts`, so server modules such as `src/lib/api.ts` are unit-testable; stub `fetch` with `vi.stubGlobal` (globals and mocks are restored after each test).
- Async server components are covered end to end, not with Testing Library.
- Playwright builds and starts the production server itself (`next build`, then `next start -p 3090`, a port of its own so a running compose stack is never reused in its place) with `API_ORIGIN` and `WORKER_ORIGIN` on a closed port, so the shell is tested with both down; flows that need data run against the composed stack (T29).
- Locators are roles and names; assertions are web-first (`toBeVisible`, `toHaveText`); no fixed waits.

## Backend harness (`PaymentPlatform.IntegrationTests/Support/`)

- `SqlServerFixture` is an xUnit v3 assembly fixture: one SQL Server container (the compose image, `2025-CU10-ubuntu-24.04`) per test run, started once, removed by Testcontainers at the end.
- `CreateDatabaseAsync()` makes a `t_<guid>` database and applies every migration with `MigrateAsync`, so migrations are exercised on every run.
- `ApiTestBase` gives each test class its own database and an `ApiFactory`; derive from it with a primary constructor taking `SqlServerFixture`.
- `ApiFactory` is a `WebApplicationFactory<Program>` over the API's real `Program` (public in .NET 10), environment `Testing`, simulated latency 0, logs at `Warning` and above.
  `ApiFactory.UnreachableDatabase` points at a closed port for the database-down paths.
- Translations: `tower::ServiceExt::oneshot` is `Factory.CreateClient()`; `tests/support/mod.rs` is `Support/`; the per-test schema is a database per test class.

## Mechanics

- Time: inject `TimeProvider`; tests use `FakeTimeProvider` and `Advance`, never `Task.Delay` or sleeps.
- Metrics: `MetricCollector<T>` from `Microsoft.Extensions.Diagnostics.Testing` over a real `IMeterFactory` (`new ServiceCollection().AddMetrics()`).
- Logs: `FakeLogger` and its `Collector` from the same package; assert levels and exceptions, not message text.
- Cancellation: `TestContext.Current.CancellationToken` in every async call, so a hung test is cancelled with the run.
- Assertions name what was wrong (`Assert.True(ok, $"readiness took {elapsed}")`); API tests assert `code`, never `detail`.
- Names are underscore sentences; CA1707 is off under `backend/tests/`.

## Known gaps

- The five-case checklist (success, validation, no token, another merchant, conflicts) is covered for login and `/me` (`Auth/`); helpers in `Support/`: `Merchants.CreateUserAsync`, `Tokens.Forge` and `WithoutSignature` for hand-made tokens with the factory's fixed 64-byte key, `Problems.ReadAsync` and `ReadCodeAsync`, `Clients.For(factory, user)` for a client with a valid token, and `LogCapture` for every log record at Trace and up.
- Security properties get a mutation check when they land: break the property on purpose (allow HS512, skip the dummy hash, drop the merchant filter, accept any card) and confirm a test fails, then restore it.
- The worker has no integration tests yet; they arrive with its first job (T23), driving the job against the fixture's database.
- No property-based tests; add FsCheck with the first rule whose input space is large (card number checks in T20).
- The log-capture test that proves no card data reaches a log line lands with T20.
