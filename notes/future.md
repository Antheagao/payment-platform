# Future

Prioritized work ahead, with the reasoning.
Move items to `past.md` as they land and update `present.md`.
Task IDs match the task board in `present.md`.
This file is also the queue for the `batch-loop` skill (`.claude/skills/batch-loop/SKILL.md`): rows are taken top to bottom, and a `[USER-GATED: ...]` or `[BLOCKED: ...]` tag means skip it until the condition after the colon is met.

Last updated: 2026-10-28.

## Now: scaffold

The goal of this phase is a walking skeleton: every project exists, builds, is wired for observability, and is covered by the gates CI runs, before any payment feature lands.
Each row is one commit and one push; a guide overlay in `.claude/skills/` lands with the code it describes, so the overlays only ever state facts.

### T1. Repository base (finishing)

- Agent rules: `AGENTS.md` at the root, `.claude/CLAUDE.md` importing it, the `batch-loop` skill, and the `payment-platform-pull-requests` overlay.
- Gate: the hooks fire (a commit without `notes/present.md` is refused), and the skill files have valid front matter.

### T2. Architecture and decisions

- `docs/requirements.md`: the exercise restated in our own words as a traceability table (requirement, where it will live, status).
- `docs/architecture.md`: components, request and async flows, the observability seams, and a Mermaid diagram.
- ADRs in `docs/decisions/`: 001 stack and repository layout, 002 backend project boundaries, 003 the async workflow on a transactional outbox, 004 authentication with short-lived JWTs behind the Next.js server, 005 observability on OpenTelemetry, 006 errors as RFC 9457 problem details with a stable `code`.
- Gate: every relative link in `docs/` resolves; no em dashes.

### T3. Backend solution and conventions

- `backend/global.json` (SDK 10.0.1xx, `latestFeature`), `Directory.Build.props` (nullable, warnings as errors, analyzers, NuGet audit), `Directory.Packages.props` (central package management), `PaymentPlatform.slnx`, `.config/dotnet-tools.json` with `dotnet-ef`, and the C# rules in `.editorconfig`.
- Overlays: `payment-platform-repository-structure`, `payment-platform-clean-code`, `payment-platform-code-comments`.
- Gate: `dotnet build backend/PaymentPlatform.slnx` succeeds.

### T4. Domain

- `PaymentPlatform.Domain`: `PaymentStatus`, the transition table (`Pending -> Authorized -> Captured -> Settled`, `Failed` and `Refunded` as terminal branches), `Money` in integer minor units with an ISO 4217 currency, and typed domain errors.
- `PaymentPlatform.Domain.Tests` (xUnit v3): every allowed and every rejected transition, money boundaries.
- Gate: `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`.

### T5. Service defaults

- `PaymentPlatform.ServiceDefaults`: JSON console logs with scopes, OpenTelemetry tracing, metrics, and logs over OTLP, `/health/live` and `/health/ready`, and the request id middleware (accept a well-formed `X-Request-Id`, otherwise generate one, echo it, and put it in the log scope).
- Overlay: `payment-platform-error-handling-logging`.
- Gate: build, tests for the request id rules.

### T6. Persistence

- `PaymentPlatform.Persistence`: `PaymentsDbContext` on the SQL Server provider, a design-time factory for `dotnet ef`, connection resiliency, and a readiness health check.
  No tables yet; the first migration lands with T19.
- Overlay: `payment-platform-database` (SQL Server translations of the global PostgreSQL rules).
- Gate: build.

### T7. Application layer

- `PaymentPlatform.Application`: the home for use cases (feature folders), `AddApplication()`, and the acquirer port with a deterministic simulated adapter for local runs and tests.
- Gate: build, unit tests for the simulated acquirer's outcomes.

### T8. API host

- `PaymentPlatform.Api`: minimal APIs under `/api/v1`, snake_case JSON, RFC 9457 problem details for every error including unknown routes, the built-in OpenAPI document, security headers, and the service defaults.
- Overlays: `payment-platform-api-design`, `payment-platform-security`.
- Gate: build; `dotnet run` answers `/health/live` and an unknown route returns `application/problem+json`.

### T9. Worker host

- `PaymentPlatform.Worker`: a slim web host for health endpoints plus a periodic job runner (timer, cancellation, per-run tracing, duration and failure metrics) that the outbox, webhook, and settlement jobs plug into later.
- Overlays: `payment-platform-architecture`, `payment-platform-design-principles`.
- Gate: build; the worker starts and answers `/health/live`.

### T10. Integration test harness

- `PaymentPlatform.IntegrationTests`: one SQL Server container per test run (Testcontainers), a database per test class, and a `WebApplicationFactory` over the real `Program`.
  First tests: liveness, readiness against a live and a dead database, and the unknown-route envelope.
- Overlay: `payment-platform-testing`.
- Gate: `dotnet test` with Docker running.

### T11. Architecture tests

- `PaymentPlatform.ArchitectureTests`: Domain references nothing but the BCL, Persistence never references a host, no host references another host.
- Gate: `dotnet test`.

### T12. Containers

- Dockerfiles for the API and the worker (multi-stage, non-root), `docker-compose.yml` with SQL Server, the Aspire dashboard as the OTLP sink, the API, and the worker; `.env.example`.
- Gate: `docker compose config -q`, `docker compose up --build`, both health endpoints green, a trace visible in the dashboard; then `docker compose down`.

### T13. Frontend scaffold

- `frontend/` from `create-next-app` (Next.js 16, TypeScript strict, Tailwind v4, ESLint, App Router, `src/`), Prettier, `server-only`.
- Gate: `npx tsc --noEmit`, `npx eslint . --max-warnings 0`, `npx next build`.

### T14. Frontend shell

- Semantic design tokens with `light-dark()`, the app layout and header, a server-only API module with timeouts, and an API status badge; the frontend Dockerfile and compose service.
- Overlay: `payment-platform-frontend-ui`.
- Gate: frontend gates plus a real-browser check in light and dark at 360px and 1440px.

### T15. Frontend tests

- Vitest with Testing Library for components and pure helpers; Playwright with one smoke test against `next build` plus `next start`.
- Gate: `npm test`, `npx playwright test`.

### T16. Continuous integration

- `.github/workflows/ci.yml`: backend (format, build, tests with SQL Server), frontend (typegen, tsc, eslint, tests, build, audit), compose validation; `dependabot.yml`; CodeQL for C# and TypeScript.
- Overlay: `payment-platform-ci-cd`.
- Gate: CI green on the pushed commit.

### T17. Infrastructure as code

- `infra/bicep/`: App Service for the API and the dashboard, the worker host, Azure SQL, Service Bus, Storage, Key Vault, Application Insights, all with managed identities.
  Validated with `az bicep build` and `bicep lint` in CI; never deployed (portfolio rule: no real deploys).
- Gate: the CI infra job is green.

### T18. README and documentation deliverables

- README: what it is, how to run it locally, the architecture in one diagram, links to docs.
- `docs/`: tradeoffs, assumptions, production considerations, future improvements, each honest about what is built and what is planned.
- Gate: links resolve; a fresh clone follows the setup steps successfully.

## Next: the payment slice

Ranked by what the exercise grades most: the lifecycle and its audit trail, idempotency, the async workflow, then the dashboard.

- **T19. Merchant accounts and login.** Merchants and users tables (first migration), password hashing, `POST /api/v1/auth/login` issuing a short-lived signed JWT, login rate limiting, a seeded demo merchant, and tests for expiry and tampering.
- **T20. Create and read payments.** Payments and payment-event (audit) tables, `POST /api/v1/payments` with `Idempotency-Key`, `GET /api/v1/payments/{id}`, merchant scoping (another merchant's payment is a 404), the simulated authorization.
- **T21. Capture and refund.** `POST /payments/{id}/capture` and `/refund`, idempotent, every transition audited with actor, time, and request id.
- **T22. List and search.** Filters by status, date range, amount, and reference; keyset pagination with an opaque cursor; the indexes that serve it.
- **T23. Outbox and webhook delivery.** Outbox rows written in the same transaction as the state change, a dispatcher claiming rows with `READPAST`, HMAC-signed webhooks, exponential backoff with jitter, a dead-letter state, and trace context carried across the hop.
- **T24. Settlement.** A scheduled batch that settles captured payments through a T-SQL stored procedure, with reconciliation counts.
- **T25. Domain and queue metrics.** Payments created, captured, refunded, failed by state; outbox depth, oldest-pending lag, retries, dead letters.
- **T26. Dashboard login.** Login form, the JWT held in an HttpOnly cookie on the Next.js server, sign-out, expiry handling.
- **T27. Payments list.** Filters and search bound to the URL, pagination, empty and error states.
- **T28. Payment detail.** Amounts, status, and the audit timeline.
- **T29. End-to-end golden path.** Playwright: log in, find a payment, open its timeline; CI job against the composed stack.
- **T30. Documentation complete.** Tradeoffs, assumptions, production considerations, and future improvements rewritten against what was actually built.
- **T31. Load test.** A k6 script in a container against the composed stack, with the numbers and the bottleneck written down.

## Later: parked

- **Python reconciliation export** for a Power BI dataset. [BLOCKED: only if the settlement work (T24) shows a pipeline is needed; the brief says Python only if needed.]
- **Azure DevOps pipeline** mirroring the GitHub Actions gates. [USER-GATED: an Azure DevOps organization to run it in; an unrun pipeline would be unverified.]
- **Real Azure deployment.** [USER-GATED: never for this repository; portfolio rule is validate only.]
