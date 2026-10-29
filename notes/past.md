# Past

A log of what has been built, in order.
Each entry names the commit(s) so the diff is one `git show` away.
Update this file when a task lands; keep the present and future notes in sync.

## 2026-10-28: repository base (T1)

- `repo: initialize repository` (`c8a4e32`): README, `.gitignore` for .NET, Next.js and Bicep output, LF line endings, root `.editorconfig`.
  Pushed to `github.com/Antheagao/payment-platform` as a public repository.
- `notes: add past, present, and future notes with rotation hooks` (`e0cb13d`): the three notes and the `.githooks/` guards that keep them honest; a test commit without `notes/present.md` was refused as intended.
- Stack decision recorded in the brief: SQL Server rather than PostgreSQL, to match the hiring team's Azure SQL.
- `agents: add agent rules, batch-loop skill, and pull-requests overlay`: `AGENTS.md` with orientation, ports, gates, and the guide table; `.claude/CLAUDE.md` imports it with `@../AGENTS.md`; the `batch-loop` skill and the `payment-platform-pull-requests` overlay.
- Lesson: in PowerShell, `git commit -F -` does not read a here-string; commit through the Bash tool with a quoted heredoc instead.

## 2026-10-28: decisions (T2, part 1)

- `docs: add decision records 001-007 and the assumptions`: stack and layout (adds `scripts/` to the top-level folders), backend project boundaries, the transactional outbox, JWT authentication behind the dashboard's server, OpenTelemetry, problem details, idempotency keys; `docs/assumptions.md`; `scripts/check-docs.mjs`.
- Pinned images chosen while writing ADR 001: `mcr.microsoft.com/mssql/server:2025-CU10-ubuntu-24.04` and `mcr.microsoft.com/dotnet/aspire-dashboard:13.6.2` (the newest on MCR on 2026-10-28; MCR's tag list sorts lexically, so `CU9` sorts after `CU10`).

## 2026-10-28: architecture overview (T2, part 2)

- `docs: add the architecture overview and requirements traceability`: `docs/architecture.md` and `docs/requirements.md`; T2 done.

## 2026-10-28: backend conventions (T3, part 1)

- `backend: add the solution and shared build conventions`: `PaymentPlatform.slnx` (the .NET 10 SDK writes `.slnx` with `--format slnx`), `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `BannedSymbols.txt` with the BannedApiAnalyzers package, the `dotnet-ef` tool manifest, and C# rules in `.editorconfig`.
- Gate: `dotnet build PaymentPlatform.slnx` succeeds (with only "no project to restore" until T4 adds one).
- Versions chosen on 2026-10-28: EF Core and ASP.NET Core packages 10.0.14, OpenTelemetry 1.20.1 (SqlClient instrumentation is now stable), xunit.v3 4.0.2 with xunit.runner.visualstudio 4.0.1, Testcontainers.MsSql 4.16.0.
- `agents: add repository-structure, clean-code, and code-comments overlays`: the C# and SQL Server translations of the three global guides (EF timestamped migrations replace `NNNN_` numbering; T-SQL objects live as embedded `.sql` files; test names are underscore sentences); T3 done.

## 2026-10-28: domain (T4)

- `backend: add the domain model with the payment lifecycle`: `PaymentPlatform.Domain` and `PaymentPlatform.Domain.Tests` (105 tests); `docs/requirements.md` marks R8 done.
- Lessons:
  - xUnit v3 (4.x) runs on Microsoft.Testing.Platform, and the .NET 10 SDK refuses the VSTest bridge; `global.json` now selects `"test": {"runner": "Microsoft.Testing.Platform"}`, and `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` are gone.
  - `latest-recommended` raises CA1000 for static factories on a generic type, so `Result<T>` is built by implicit conversion; CS1573 (partial parameter docs) is off so only meaningful parameters get docs.
  - Private static readonly fields needed their own PascalCase naming rule; the editorconfig rule with more modifiers wins.
  - A probe file using `DateTime.UtcNow` failed the build with RS0030, so the banned-API rule is live.

## 2026-10-28: service defaults (T5)

- `backend: add service defaults for logs, telemetry, health, and request ids`: `PaymentPlatform.ServiceDefaults` and its 18 tests; the `payment-platform-error-handling-logging` overlay.
- Log properties are PascalCase (`RequestId`, beside the `TraceId` and `SpanId` that .NET adds), so `docs/architecture.md` now says so; API JSON stays snake_case (`request_id` in problem details).

## 2026-10-28: persistence (T6)

- `backend: add persistence on EF Core and SQL Server`: `PaymentPlatform.Persistence` (context, registration, readiness check, design-time factory), SQL client tracing in the service defaults, and the `payment-platform-database` overlay.
- Gate: build, the 123 existing tests, format; `dotnet ef migrations list --no-connect` and `dotnet ef dbcontext info` both build the context with no database running.
- Lessons:
  - NuGet audit did its job on the first SQL Server reference: Microsoft.Data.SqlClient 6.1.7 pulls `System.Security.Cryptography.Pkcs` 9.0.11 (GHSA-846f-f764-76mx, high), and the build failed until the transitive pin to 10.0.14.
  - `dotnet ef` takes `--configuration`, not `-c`, and reads Debug output by default.
  - The design-time factory first held the local SA password; it now holds none, since `migrations add` never connects and the repository is public.

## 2026-10-28: application layer (T7)

- `backend: add the application layer with the acquirer port`: `PaymentPlatform.Application` and its 11 tests.
- Design note: the simulated acquirer is stateless on purpose. The API and the worker run separate processes, so an in-memory record of authorizations would make the worker's reconciliation disagree with the API; deriving the outcome from the token and the reference from the payment id makes every process give the same answer.

## 2026-10-28: correlation id (found while gating T8)

- `backend: carry a correlation id and log each failed probe once`: running the API for the first time showed three defects, all fixed in this commit.
  - ASP.NET Core already writes a `RequestId` (connection id plus counter) into every log scope, so ours appeared as a second `RequestId` with a different value.
    The id is now the correlation id: `X-Correlation-Id`, the `CorrelationId` scope, `correlation_id` in problem details; ADRs 002, 003, 005, and 006 carry a dated amendment.
  - Our scope was a `Dictionary`, so its `Message` printed as a type name; a one-entry scope type now prints `CorrelationId:<id>`.
  - A failed readiness probe logged twice (our warning and the health check service's error); the check now returns its description and exception and logs nothing.
  - `MapHealthChecks` answered every method, so `DELETE /health/live` was a 200; both probes are now GET and HEAD only.
- Lesson: Bash tool commands over about 8 KB still fail to parse on this machine; long edit scripts go in a scratchpad file.

## 2026-10-28: API host (T8)

- `backend: add the API host with problem details and security headers`: `PaymentPlatform.Api`, and the `payment-platform-api-design` and `payment-platform-security` overlays.
- Gate (run against the Release build with no database): `/health/live` 200 with `nosniff`, CSP, and `X-Correlation-Id`; `/health/ready` 503 in 3.3 s with a 3 s connect timeout; `GET /api/v1/payments` and `DELETE /health/live` return `application/problem+json` with `code`, `correlation_id`, and `trace_id`; a caller's correlation id is echoed; `/openapi/v1.json` is 200; logs are JSON lines.
- Cosmetic, left as is: EF Core warns at startup that no entity configurations exist; the warning goes away with the first table (T19).

## 2026-10-28: worker host (T9)

- `backend: add the worker host and the periodic job runner`: `PaymentPlatform.Worker` with `PeriodicJob` and `WorkerTelemetry`, 5 tests, and the `payment-platform-architecture` and `payment-platform-design-principles` overlays.
- Gate: build, 141 tests three times in a row, format; the worker started against no database and answered `/health/live` 200 and `/health/ready` 503, with JSON logs and no `Server` header.
- Writing the design overlay exposed that every job would have created its own DI scope; `PeriodicJob` now hands each run a fresh scope, with a test.

## 2026-10-28: integration test harness (T10)

- `backend: add integration tests on SQL Server with Testcontainers`: `PaymentPlatform.IntegrationTests` (assembly-fixture container, a migrated database per test class, `ApiFactory` over the real `Program`), 12 tests, and the `payment-platform-testing` overlay; `docs/requirements.md`: R14 and R23 done, R25 scaffolded.
- Docker Desktop was not running at the start of the session; starting it also restarted other projects' containers through their restart policies (doc-pilot, ecommerce-api, workout, recipe), which were left alone.
- In .NET 10 the top-level `Program` is public, so `WebApplicationFactory<Program>` needs no `public partial class Program`.

## 2026-10-28: architecture tests (T11)

- `backend: enforce the project boundaries with architecture tests`: `PaymentPlatform.ArchitectureTests`, 16 tests, no extra package (reflection over the loaded assemblies plus the `.csproj` files).
- Gate: 169 tests green; a probe that unsealed `Actor` failed `Concrete_classes_are_sealed...` and was reverted.
- Lesson: compiler-generated closures (`<>c__DisplayClass1_0`) have public mutable fields, so type rules must skip compiler output.

## 2026-10-28: containers (T12)

- `infra: add container images and the compose stack`: `backend/Dockerfile` (one restore, `api` and `worker` targets, chiseled non-root runtime), `backend/.dockerignore`, `docker-compose.yml`, `.env.example`, `scripts/sql/create-local-database.sql`, `Persistence/DatabaseMigrator` behind `Database:MigrateOnStartup`.
- Gate: `docker compose config -q`; `docker compose build` (45 s cold); `docker compose up`: both readiness probes green, the bootstrap exited 0, the API logged its startup migration, and the Aspire dashboard showed the API's traces and structured logs in a real browser; `docker compose down`.
- Found while gating: `AddService(serviceName: ...)` silently overrode the standard `OTEL_SERVICE_NAME`, so compose's `payment-api` never appeared; the service defaults now read the variable first.
- The app never connects as `sa`: the bootstrap creates a `payments_app` login that owns only the `payments` database.

## 2026-10-28: frontend scaffold (T13)

- `frontend: scaffold the dashboard with Next.js 16, TypeScript, and Tailwind v4`: `create-next-app@16.4.3 --ts --tailwind --eslint --app --src-dir --react-compiler`, the boilerplate assets removed, Prettier added, strict scripts, a placeholder page.
- Gate: `npm run typecheck`, `npm run lint`, `npm run format:check`, `npm run build` (two static routes), `npm audit --omit=dev` with 0 vulnerabilities.
- Surprises: create-next-app 16.4 enables `cacheComponents` and `partialPrefetching` without being asked, and wires Tailwind through `@tailwindcss/turbopack` instead of PostCSS; both kept (the installed docs say both flags become the only behavior in the next major).
  Its `.gitignore` ignored every `.env*`, including the example; it now keeps `.env.example`.
- `scripts: check generated docs for links only`: the T13 commit (`006728f`) went out with the docs gate failing on em dashes in `frontend/AGENTS.md`, which `next dev` writes and nobody may edit, because a failed edit and the commit sat on separate lines of one shell command.
  The gate now checks generated files for links only, and the em dash it searches for is built from its code point so the character never appears in the repository.
  Lesson: chain every gate and the commit with `&&` in one line, or the commit runs after a failure.

## 2026-10-28: frontend shell (T14)

- `frontend: add the dashboard shell, design tokens, and container image`: tokens, primitives, header with the streamed API status, lifecycle legend, error and not-found pages, security headers, the server-only API module, `frontend/Dockerfile`, the compose service, `scripts/check-contrast.mjs`, and the `payment-platform-frontend-ui` overlay.
- Gate: typecheck, lint, format, build (both routes partially prerendered), `npm audit --omit=dev` 0; contrast 36 of 36 pairs with all 19 tokens in gamut; in a real browser through compose at 1440 and 360, light and dark, no horizontal overflow, the API-down badge, and the skip link.
- Found in the browser and fixed before committing:
  - port 3000 already belonged to the doc-pilot container, and my first curl had reached doc-pilot; the dashboard moved to 3080 everywhere.
  - Authorized (blue 245) and Captured (indigo 265) looked identical; Authorized is now teal (200).
  - the neutral Refunded pill vanished on its gray row; neutral pills now carry an inset ring.
  - the focused skip link had no padding, because Tailwind's `not-sr-only` resets it; the padding is now focus-scoped.
- The contrast checker reads `globals.css` instead of keeping its own copy of the palette (cosgift's script duplicates it), so the stylesheet stays the only source of truth.

## 2026-10-28: frontend tests (T15)

- `frontend: add Vitest unit tests and Playwright golden paths`: `vitest.config.mts`, `test/`, 16 unit and component tests, `playwright.config.ts`, `e2e/home.spec.ts` (12 runs), `test`, `test:watch`, and `e2e` scripts, and the frontend section of the testing overlay.
- Gate: `npm test` 16/16, `npm run e2e` 12/12 in 9 s, typecheck, lint, format; the container image still serves pages and CSS.
- Two warnings fixed rather than ignored:
  - Vite now resolves tsconfig paths itself, so `vite-tsconfig-paths` is gone in favor of `resolve.tsconfigPaths`.
  - `next start` does not support `output: "standalone"`; standalone is now built only when `NEXT_OUTPUT=standalone`, which the Dockerfile sets.

## 2026-10-28: continuous integration (T16, part 1)

- `ci: add GitHub Actions, CodeQL, and Dependabot`: the three files above; current majors `actions/checkout@v7`, `setup-dotnet@v6`, `setup-node@v7`, `upload-artifact@v7`, `codeql-action@v4`.
- Before pushing: `CI=true dotnet restore --locked-mode` succeeded locally, so the lock files written on Windows hold for a Linux runner as far as Windows can tell.
- `backend: drop an unused using that newer SDKs flag`: the first CI run failed only in `dotnet format` on an unused `using System.Diagnostics;` in `ServiceDefaultsExtensions.cs`; CI installs SDK 10.0.403 while this machine has 10.0.101, which never reported it.
  Lesson: CI's SDK is the authority until this machine's SDK is updated; the other four jobs (frontend, e2e, repository, containers) were green on the first run.

## 2026-10-28: continuous integration (T16, part 2)

- `ci: hold back deliberate majors and add the ci-cd overlay`: CI run 40790028817 on `85a1598` was green in all five jobs and CodeQL found nothing; Dependabot's first scan opened #1 to #5, four of them majors that would break deliberate pins, so `dependabot.yml` now ignores majors for Node (image and types), TypeScript, ESLint, and the .NET images; the `payment-platform-ci-cd` overlay records the jobs, how to reproduce each, the toolchain drift, and the Dependabot policy.
- The open Dependabot pull requests were not closed or merged: that is the owner's call.

## 2026-10-28: infrastructure as code (T17)

- `infra: describe the Azure shape in Bicep, validated in CI`: `infra/bicep/` (main, eight modules, parameters, linter config), `infra/sql/grant-app-identities.sql`, `infra/README.md`, the `infra` CI job, and the infrastructure section of the ci-cd overlay.
- Gate: the Bicep CLI 0.48.1 (the official release binary, downloaded to the session scratchpad, since this machine has no Azure CLI) builds and lints with no diagnostics and builds the parameter file; a probe with an unused parameter and a hard-coded management URL failed lint with two errors; CI runs the same three commands.
- Choices worth knowing: no secret anywhere (managed identities for SQL, Service Bus, Key Vault, and Application Insights; the JWT key is a Key Vault reference); the worker site refuses inbound traffic; the Service Bus host comes from the resource's own endpoint rather than a hard-coded domain; Service Bus defaults to Premium because only Premium supports private endpoints.

## 2026-10-28: README and documentation deliverables (T18)

- `docs: write the README and the documentation deliverables`: `README.md` (badges, status, run it, develop, diagram, repository, documentation, how it is built), `docs/tradeoffs.md`, `docs/production.md`, `docs/future-improvements.md`; `docs/requirements.md` marks D1, D3, D5, and D6 done; ADR 005 links the production page.
- Gate: docs links resolve; a fresh clone of `83577a5` from GitHub into the scratchpad, followed exactly as the README says (`cp .env.example .env`, `docker compose up --build`), brought every service up with both readiness probes green, the dashboard showing "API ready", OpenAPI served, and the Aspire dashboard answering; then `docker compose down`.
  The clone reused the existing `payment-platform_sqlserver-data` volume (same compose project name); the first-ever bootstrap on an empty volume was exercised in T12.
- CI run 40790395009 on `83577a5`: all six jobs green, including the new Bicep job.
- The scaffold queue (T1 to T18) is empty; the run stopped here because the owner asked for the initial structure, and `future.md` now opens on T19 with its first slice laid out.

## 2026-10-28: readiness answers by its deadline (T32)

- Found while rehearsing the outage demo for the one-command start: with the compose database stopped, `/health/ready` took 18 s on the first probe and 5 s on each one after, so the dashboard's 3 s timeout reported "API unreachable" instead of "API not ready".
- Causes, measured: the check went through EF Core's retrying execution strategy (it waited out the outage it should report) with SqlClient's default 15 s connect timeout; and SqlClient resolves host names synchronously inside `OpenAsync`, which for a stopped container under Docker took 3 to 4 s (`getent ahosts sqlserver` from a sibling container), so no cancellation token could cut it short.
- `backend: answer readiness by its deadline when the database is gone`: the check runs on its own context options (no retries, 2 s connect and command timeouts), starts on the thread pool, and stops waiting at its 3 s deadline; the dashboard waits 5 s.
  The unreachable-database test now keeps the client's default timeouts, as compose does, and probes three times; a silent server (a listener that never replies) and a connection that blocks its thread round out the cases.
  The blocking-thread test failed (timed out) without the thread-pool start and without the deadline, and passes with both.
- Gate: backend format, Release build, 171 tests; frontend typecheck, lint, format, 16 unit tests; in compose, two outages in a row: the first probe answers at 3.0 s, later ones in under 1 s, the dashboard shows "API not ready", and readiness recovers within 15 s of the database starting.

## 2026-10-28: one-command start with no setup (T33)

- The owner asked for one easy command that starts everything, nothing paid, and a demo-ready stack (requirements 8 to 10 in `present.md`).
- `infra: start the whole stack with one command and no setup`: `docker-compose.yml` gives the local-only passwords defaults (already public in `.env.example`, so nothing new is exposed) and makes every host port a `*_PORT` variable; SQL Server is pinned to `linux/amd64`; `.env.example` is now optional overrides, all commented out; `scripts/check-stack.sh` waits for both readiness probes, OpenAPI, the dashboard showing "API ready", and the Aspire dashboard, then prints the URLs; the CI `containers` job becomes `stack`, which runs the README's command on a clean runner with no `.env`, then the check, uploads the logs on a failure, and always removes everything.
- `docker compose up --wait` treats the one-shot `sqlserver-init` as done once it exits 0 (Compose 2.40); the .NET services have no container healthcheck (the chiseled images have no shell or curl), so `--wait` returns once they run and `check-stack.sh` waits for their readiness.
- Gate: `docker compose config` with an empty env file shows the defaults, and with an override file the overrides (a moved dashboard port, another application password); `docker compose up --build -d --wait` with an empty env file returned in 24 s on a warm cache and `check-stack.sh` passed at once; docs links.
