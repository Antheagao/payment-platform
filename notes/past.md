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

## 2026-10-28: prerequisites, free to run, troubleshooting (T34)

- `docs: list every download and show nothing needs a paid service`: the README gains Prerequisites (to run: Git and Docker; to develop: the .NET SDK, Node.js 22, Playwright's Chromium, Bicep), Free to run (each piece and its cost), and If something goes wrong (six symptoms and their fixes); requirements O2 and O3 done.
- Verified rather than recalled: every `winget` id with `winget show` (Git.Git 2.56, Docker.DockerDesktop 4.97, Microsoft.DotNet.SDK.10 10.0.403, OpenJS.NodeJS.22 22.23, Microsoft.Bicep 0.48.1); every Homebrew name against formulae.brew.sh (`docker-desktop` and `dotnet-sdk` casks, `node@22`, `git`; the old `docker` cask is gone; Bicep is in the `azure/bicep` tap); Docker Desktop's free-use terms from its license page; the port-conflict error by binding one port twice; the Windows daemon-down error from this machine.
- Licenses: all 130 resolved NuGet packages are MIT or Apache 2.0 except the SqlClient SNI runtime and MSAL's native interop, both under free Microsoft license terms; every npm package is under an open-source license (MIT, ISC, Apache 2.0, BSD, MPL 2.0, BlueOak, and libvips under LGPL 3.0 for `sharp`).
- Fresh clone of `7c6f549` from GitHub with no `.env`, as project `pp-fresh` so the database volume was new: `docker compose up --build -d --wait` in 29 s on a warm build cache, `check-stack.sh` green at once, the bootstrap created the database and login, the API migrated; then a `.env` with Windows line endings moved the dashboard to 3090 and the API to 5090, and both the stack and `check-stack.sh` followed; `down -v` removed that project's volume.
- CI run 40801177285 on `7c6f549`: all six jobs green; the new `stack` job went from an empty runner to every service answering in 77 s (73 s of it the build and start).

## 2026-10-28: a healthy start logs no warnings (T36)

- Found while checking the Aspire dashboard for the demo guide: every API start logged an EF Core warning (event 10632, no `IEntityTypeConfiguration` found in the Persistence assembly), the one highlighted row in an otherwise clean structured-log view.
- `persistence: keep a healthy start free of warnings`: `PaymentsDbContext` stops scanning for configurations while there are none; T19's first slice now names restoring `ApplyConfigurationsFromAssembly` with the first configuration.
  `Startup/StartupTests` starts the API the way compose does (migrating at startup) with fake logging and asserts nothing at Warning or above; it failed on exactly this warning before the change.
- Gate: locked restore, format, Release build, 172 tests; in compose, a fresh Aspire dashboard and a rebuilt API show no warning in the container logs or the structured-log view.

## 2026-10-28: demo guide and the sample-data plan (T35)

- `docs: add a demo guide and plan the sample data`: `docs/demo.md` walks the running stack in seven steps (one command; the dashboard; problem details with a correlation id; the same request's trace, logs, and metrics in the Aspire dashboard; a database outage and recovery, then an API outage; the tests and CI; what comes next), linked from the README and requirement O4.
- Every step was run against the composed stack in this session: the 404 and 405 bodies and headers; `/traces/detail/<trace_id>` opens the request's trace; the structured logs show `Database migrated at startup`; the Metrics page lists `http.server.request.duration`, Kestrel, and the runtime for `payment-api`; the outage gives `503` at the deadline, "API not ready", and recovery in about 15 s; the backend tests take 27.6 s.
- Sample data needs the payment tables, which do not exist yet, so it is planned rather than faked: the T19 row now specifies the `Demo:Seed` switch (compose only, refused in Production, idempotent by fixed ids, demo login shown in Development), T20 seeds payments in every status through the domain so each has a real audit trail, T21 routes the seed through capture and refund, T22 seeds enough rows to page, and every feature row updates `docs/demo.md`.
  Requirement O4 is Scaffolded until then.
- Checked in a real browser (`chrome-devtools-axi`): the dashboard at 1440 light and 360 dark, and the Aspire trace, structured-log, and metrics pages.
  The Aspire "Endpoint is unsecured" banner is expected for a local, anonymous dashboard bound to 127.0.0.1; the guide says so instead of adding a public API key that would protect nothing.

## 2026-10-28: balanced status colors (T37)

- Found in the first browser pass for the screenshots: in dark mode the Captured and Failed pills read dimmer than the other four (contrast 5.9 and 6.3 against 7.7 to 8.6), so equal statuses looked unequal.
- `frontend: balance the dark status colors and gate their spread`: dark `primary` 0.72 to 0.78 lightness (hover and soft follow), dark `danger` 0.75 to 0.79 with its soft at 0.27 like the others; light mode was already within 1.21.
  `scripts/check-contrast.mjs` now also fails when, within a scheme, the strongest pill's ratio exceeds the weakest's by more than 1.25; it fails on the old dark palette (1.47) and passes the new one (1.15).
- Gate: `node scripts/check-contrast.mjs` (19 tokens in gamut, 36 pairs, pills balanced in both schemes).

## 2026-10-28: live service status and the lifecycle flow (T38)

- The first browser pass (1440 and 360, light and dark) found a clean but sparse page: a heading and one legend card, with most of a desktop screen empty, and nothing that showed the running system.
- `frontend: show live service status and draw the lifecycle as a flow`: the home page gains a service status panel built only from real answers (the API's and the worker's readiness probes, the API's `database` check, and round trips timed by the dashboard's server), refreshed every 5 s while the tab is visible, with Pause for WCAG 2.2.2; the lifecycle becomes four steps joined by arrows (down the gap when stacked) with each terminal branch under the step it leaves.
  `api.ts` returns the checks and timings and is `cache()`d per request; `Button` gains a 32px size; compose passes `WORKER_ORIGIN`.
- Fixed on the way: Playwright reused any server already on port 3080, so with the stack running locally it tested the composed dashboard against a live API instead of the fresh build, and two checks failed for that reason alone; it now builds and serves on 3090.
- Checked in a real browser: both themes at 1440 and 360 with no sideways scroll; the database stopped (Not ready, Unavailable, Not ready) and the API stopped (Unreachable, Unknown, Ready) without a reload; the streaming fallback and the answered tiles both 174px tall; focus visible on Pause after the skip link and the logo.
- Gate: typecheck, lint, format, Vitest 30/30, build (`/` partially prerendered), `npm audit --omit=dev` clean, Playwright 16/16, contrast, docs.

## 2026-10-28: screenshots and the outage GIF (T39)

- `docs: show the dashboard and Aspire in captured screenshots and an outage GIF`: `scripts/capture-screenshots.mjs` captures the dashboard (1280 and 390, light and dark, database down, API down) and Aspire (the request's span, the outage's error logs, request durations) from the running stack, and records the database outage and recovery as `docs/images/outage.gif`.
  The README opens with the dashboard (a `<picture>` that follows the reader's GitHub theme) and gains A look around and an optional FFmpeg prerequisite; `docs/demo.md` shows each step's screen and how to remake the images; requirement O5 is done.
- `check-docs.mjs` now also resolves `src` and `srcset` in Markdown, so a renamed screenshot fails the gate; a probe file with a missing image failed it.
- Lessons:
  - Playwright's WebM is noisy enough that every GIF frame differed: the first GIF was 5.6 MB at 345 frames. Reduced motion (the live dot stops pulsing), a recording cropped to the status panel, and `mpdecimate` with a variable frame rate brought it to 0.5 MB in 11 frames with the timing intact.
  - Aspire's "Endpoint is unsecured" close button is a `fluent-button` that Playwright's role queries do not match (the first run kept the banner); the script clicks it by class and waits for the banner to go.
  - The first probe after `docker compose stop sqlserver` takes the full 3 s deadline (the framework logs that timeout as "threw an unhandled exception"); later probes fail in about 2 s on the connect timeout.
- Gate: docs, contrast; every image opened and checked; the stack was left running for the remaining checks and stopped at the end.

## 2026-10-28: requirement statuses re-checked (T40)

- The owner asked for the requirements table to be corrected and every requirement checked for correctness as the loop works through them; this batch re-checked the three rows the comparison had flagged against the code, not the docs.
- `backend: leave health probes out of the request metrics, and test the RED signals`:
  - R18 (RED) was wired but untested, and wrong in one way: readiness and liveness probes were counted in `http.server.request.duration`, so the database outage showed in the Aspire dashboard as a run of 503s in the API's request metrics (the `error.type` 503 filter in the T39 screenshot). Both health endpoints now call `DisableHttpMetrics()`.
  - `Telemetry/RequestMetricsTests` adds an in-memory exporter to the API's own OpenTelemetry pipeline (`OpenTelemetry.Exporter.InMemory` 1.20.1) and asserts 404s and 405s are recorded with status and duration, and that probes are not; the probe test failed before the fix.
  - In compose, after a minute's export, Aspire's `http.server.request.duration` lists only the 404s and `/openapi/{documentName}.json`, with no health route, despite probes every 5 s.
- R22 (the tracing story) is Done: ADR 005, the architecture's trace-continuity section, and production.md's sampling already tell it. R16 (log levels) is Scaffolded: the policy, the framework categories at `Warning`, `LoggerMessage` messages, and the healthy-start test exist; the business events arrive with T20 and T21.
- The logging overlay's known gaps were stale (problem details, SQL client spans, and worker job spans exist); they now list what is actually missing. ADR 005 gains an amendment for the probe exclusion.
- Lesson: OpenTelemetry's meter listener is process-wide, so an in-memory exporter in one `WebApplicationFactory` also sees the requests of test classes running in parallel; the first run counted three 404s for two. The tests assert lower bounds and absence by route.
- Gate: locked restore, format, Release build, 174 tests green three runs in a row; docs.

## 2026-10-28: merchant accounts and login (T19)

- `backend: add merchant accounts, login with short-lived JWTs, and the demo merchant`: R1 and R7 are done on the API side; the dashboard's sign-in is T26.
  - Domain `Merchants/` (`Merchant`, `MerchantUser`); Persistence configurations and the migrations `InitialSchema` and `EnableReadCommittedSnapshot`; Application `Auth/` (`LogIn`, `GetCurrentUser`, `PasswordVerifier`) and `Demo/DemoSeed`; Api `Auth/` (JwtBearer, `TokenIssuer`, `CallerClaims`, `LoginThrottle`, options) and `Endpoints/AuthEndpoints.cs` (`POST /api/v1/auth/login`, `GET /api/v1/me`).
  - 57 new tests (15 domain, 42 integration); two deliberate mutations (allowing HS512, skipping the dummy hash) each failed a test.
- Found on the way:
  - The test fixture migrated with plain `UseSqlServer`, so the history went to `dbo.__EFMigrationsHistory` while the API reads `payments.__EFMigrationsHistory`: with the first real migration, readiness reported it pending and the startup migration tried to create `Merchants` twice. The fixture now uses the API's provider settings.
  - The sealed-class architecture rule flagged EF's generated migration classes; `Migrations/` is now `generated_code` in `.editorconfig` and skipped by the rule.
  - In the Linux container, the first start logged three warnings that no test saw: EF Core's warning for a migration that mixes a non-transactional operation (read committed snapshot) with transactional ones, and Data Protection's two warnings about unpersisted, unencrypted keys (on Windows the keys are stored and DPAPI-protected, so the tests stayed quiet). The snapshot setting moved to its own migration, Data Protection is at `Error` because the API protects nothing with it, and `check-stack.sh` now fails on any warning before "Application started", so CI's stack job catches this class.
  - 400s for unreadable bodies and 415s had no `detail`, and 429s had no `type` (RFC 9110 does not define 429); both are filled in.
- Lessons:
  - Minimal APIs throw on a body that does not bind when the environment is Development (`RouteHandlerOptions.ThrowOnBadRequest`); it is off, so every environment answers `400`.
  - `docker compose up --wait` returns before a .NET service listens (they have no container healthcheck); scripts wait for `/health/ready`.
- Gate: format, locked restore, Release build, 231 backend tests green three runs in a row; from an empty compose volume, `check-stack.sh` green with no startup warnings, and the demo guide's new step 4 run as written.

## 2026-10-28: create and read payments (T20a)

- `backend: create and read payments with card tokenization and the audit trail`: R2, R3, R17, R24, and R25 are done.
  - Domain: `PaymentCard` and `CardBrand`; `Payment.Create` takes the card and an optional merchant reference; `Authorize` keeps the acquirer's reference and `Fail` the decline code.
  - Persistence: `Payments` (nonclustered key, clustered on `(MerchantId, CreatedAt, Id)`, `Money` and `PaymentCard` as complex properties, `CHECK`s generated from the enums, a shadow `RowVersion`) and `PaymentEvents` (identity key, the actor, the correlation id); `RecordChanges` plus a `SaveChanges` guard that refuses a payment with unrecorded changes.
  - Application: `Cards/` (`CardInput`, `ICardVault`, `SimulatedCardVault` with only test cards) and `Payments/` (`CreatePayment`, `GetPayment`, `PaymentDetails`); Api `Endpoints/PaymentEndpoints.cs` and the shared `Errors/ApiProblems`; ADR 008.
  - 75 new tests (13 domain, 23 card unit tests, 39 integration); mutations that dropped the merchant filter on reads and let the vault accept any card each failed a test.
- Found on the way:
  - The container build failed on CA1861 in the generated `Payments` migration though the local build passed: the root `.editorconfig` that marks migrations `generated_code` is outside the image's build context (`backend/`). The rule moved to `Migrations/.editorconfig`; CI's stack job would have failed on this push.
  - `GET /api/v1/payments` used to be the unknown-route example in `ErrorResponseTests`; with `POST /payments` mapped it is a 405, so the test uses a path that does not exist.
  - The no-card-data test was flaky: its first version searched the response for the CVV `739` as a plain substring and once found it inside a random correlation id. It now looks for the CVV only as a whole JSON value (and for any `cvv` field), in the responses and the stored rows alike.
- Checked against the composed stack as the demo merchant: every test card (authorized, `card_declined`, `insufficient_funds`, `pending` for the silent acquirer, `422` for `4111 1111 1111 1111`), a JPY payment read back with its history, and in the Aspire dashboard each create is one trace of `POST /api/v1/payments` with the insert, `acquirer authorize`, and the second save as children (the silent-acquirer trace has no second save).
- Gate: format, locked restore, Release build, 306 backend tests green three runs in a row; `check-stack.sh` with no startup warnings; docs.

## 2026-10-28: idempotency keys for create (T20b)

- `backend: make payment creation idempotent with Idempotency-Key`: R26 is done for create (capture and refund reuse it in T21).
  - Persistence `IdempotencyKey` and its migration; Application `Idempotency/` (`IdempotencyStore`, `DeleteExpiredIdempotencyKeys`); `CreatePayment` attaches its payment to the key in the transaction that first saves it and resumes a taken-over payment; `CreatePaymentRequest.Fingerprint()`; the endpoint validates, claims, works, and stores the exact bytes it sends; Worker `DeleteExpiredIdempotencyKeysJob`; ADR 007 gains an amendment for what was decided while building it.
  - 18 new integration tests; mutations that skipped the fingerprint check and skipped the resume each failed a test.
- Decided while building, against the ADR as first written: the fingerprint leaves out the card number and CVV (their hash would be card data), and a request refused before any work stores nothing (Stripe's rule), so a corrected request may reuse the key.
- Found on the way:
  - The worker's first job runs at startup, while compose's API may still be migrating; on an empty volume it could have queried a table that did not exist yet and logged an error the startup gate would catch. `PeriodicJob` now skips a run while the database has migrations to apply, the rule readiness already applies to traffic.
  - `RequestMetricsTests` failed about once in ten full runs: OpenTelemetry listens to a meter by name across the process, so other test hosts' `Microsoft.AspNetCore.Hosting` meters interfered with the points this one recorded. The class now runs in a collection with parallelization off; eight full runs in a row were green after.
- Checked against a fresh compose volume: the startup gate green for both services, the worker's job started; no key `400`, a retry replayed byte for byte with `Idempotent-Replayed: true`, a different amount `422`, and eight parallel requests with one key made one payment (six `409`, the first `201`, one replay).
- Gate: format, locked restore, Release build, 324 backend tests green eight runs in a row; docs.

## 2026-10-28: sample payments (T20c)

- `demo: seed thirty sample payments in every status`: `Application/Demo/DemoPayments.cs` lists them (ten settled, five captured, four authorized, two refunded from each of captured and settled, two declined, two for insufficient funds, three left pending), and `DemoSeed` adds whichever are missing.
  Each is walked through the domain (`Create`, `Authorize`, `Capture`, `Settle` as the system actor `settlement`, `Refund`, `Fail`) and recorded with `RecordChanges`, so its history is a real audit trail; ids, amounts, references, and cards are fixed, and authorized ones carry the simulated acquirer's own reference format so T21 can capture them for real.
  `docs/demo.md` names five; requirement O4's data is done, and its merchant-dashboard half waits for T27 and T28.
- Gate: 2 new tests (every status present, every history a legal and time-ordered path ending in the payment's status, settlement by the system actor, all within 90 days; a seeded payment read back through the API with its five-step trail), and the restart test now also counts thirty payments after two starts; 326 backend tests green three runs in a row.
  In compose, the existing volume gained exactly thirty records and `INV-1004` reads back as pending, authorized, captured, settled by `system`, and refunded, on three different days.

## 2026-10-28: capture and refund (T21)

- `backend: capture and refund payments, idempotently`: R4, R5, and R26 are done.
  - `Application/Payments/MovePayment.cs`: `CapturePayment` and `RefundPayment` share one flow (load the merchant's payment, check the lifecycle, call the acquirer outside any transaction with the stored authorization reference, apply the move, save it with its audit row); a decline or no answer leaves the payment unchanged; a `DbUpdateConcurrencyException` from the row version becomes the lifecycle's `409`.
  - `PaymentEndpoints`: the create flow became `RunIdempotentAsync`, shared by all three money-moving calls; `ProblemFor` maps the use cases' codes to statuses; ADR 007 gains the capture and refund amendment.
  - 14 new integration tests; a mutation that dropped the row-version handling made the racing-captures test fail.
- Checked against compose as the demo merchant: `INV-1025` captured (and the same key replayed), then refunded; the settled `INV-1001` refunded; capturing the captured `INV-1016` or the failed `INV-1006` was `409 payment_not_capturable`; no startup warnings after the rebuild.
- Gate: format, Release build, 340 backend tests green three runs in a row (the capture tests five more times alone).

## 2026-10-29: list and search payments (T22)

- `backend: list and search payments with keyset pages`: R6 is done.
  - `Application/Payments/ListPayments.cs` (`ListPaymentsRequest.Parse`, `PageCursor`, `ListPayments`) and `GET /api/v1/payments`: newest first, `limit` 1 to 100, an opaque base64url cursor, filters by status, creation time, amount, currency, and reference prefix, every bad value a field code.
  - Migration `PaymentListKeyset`: a shadow `bigint` identity `Sequence` on `Payments`, the clustered index moved to `(MerchantId, CreatedAt, Sequence)`, and indexes for the status and reference filters.
  - `DemoPayments` grows to 280 (250 generated from a fixed hash, a path shortened where it would end in the future), so the demo has pages to turn.
  - 26 new integration tests; a mutation that dropped the same-instant tiebreak lost payments between pages and failed the ties test.
- Decided while building: the tiebreak is an identity column, not the GUID id. SQL Server orders `uniqueidentifier` by its own byte order, EF cannot translate `<` on the value-converted id, and the raw-SQL keyset that would have kept the id failed: EF Core 10 names complex-type columns by their defaults (`Amount_AmountMinor`) when a query composes over `FromSql`, a 500 on every second page that the tests caught.
- Found on the way: after `dotnet ef migrations remove` of a migration already applied to the compose database (`PaymentListIndexes`, applied while debugging), the API crashed at startup on its replacement (`index ... already exists`). A removed migration is gone from the code but not from a database it touched; the local volume was reset. Nothing pushed ever carried it.
- Checked on a fresh compose volume: five migrations applied, 282 demo records seeded, all 280 payments paged once in six pages of 50, `status=refunded,failed&currency=USD` returned only those, `limit=0` named the parameter; `SHOWPLAN_TEXT` shows the list, status, and reference queries as a backward clustered seek on `MerchantId`.
- Gate: format, locked restore, Release build, 366 backend tests green three runs in a row; `check-stack.sh` with no startup warnings.


## 2026-10-29: the transactional outbox (T23a)

- `backend: write payment events to a transactional outbox and dispatch them`: R21 is done; R9 and R15 are scaffolded until signed delivery (T23b).
  - `OutboxMessages`, `WebhookEndpoints`, and `WebhookDeliveries` (migration `Outbox`); `RecordChanges` writes a `payment.<status>` event for every status change after creation, in the change's own transaction, with the correlation id, the request's `traceparent`, and a snake_case payload that holds no card data; the demo seed writes none.
  - `DispatchOutboxMessages` and the `dispatch-outbox` job (every second, draining full batches): `SELECT TOP (100) ... WITH (UPDLOCK, READPAST, ROWLOCK)` in the transaction that writes one delivery per enabled endpoint and marks the messages dispatched; each event gets an `outbox dispatch` `Consumer` span parented to its request, with `correlation.id`.
  - ADR 003 amended: transaction-held locks instead of a lease for dispatch, because dispatch does no network work; deliveries keep the lease.
  - 16 new tests (7 outbox, 1 model mapping, 5 sampler, 3 worker); mutations that dropped the lock hints, `READPAST`, the trace parent, the `Enabled` filter, the outbox write, the seed's opt-out, the sampler, its parent-based wrapper, and the dispatcher's opt-out each failed a test.
- Found on the way: the first `Outbox` migration had no `OccurredAt` or `CreatedAt` columns. EF Core maps a get-only property only when a configuration names it and skips any other one without a warning; `ModelMappingTests` now fails on any public property of a stored type without a column.
- Found on the way: the Aspire dashboard showed the every-second dispatcher as a trace a second (266 in a few minutes), burying the request traces. `TraceSampling` (ServiceDefaults, ADR 005 amended) drops a root span tagged `sampling.priority` `0` with its children; `PeriodicJob.TraceRuns => false` sets it, and the events a run dispatches stay traced under their requests. After the change, twenty seconds of the stack made five traces.
- Checked on the compose stack, upgraded from the T22 volume: the migration applied, events dispatched 0.4 to 0.5 s after the change, no deliveries without an endpoint and one `Pending` delivery with one, no outbox rows from the seed, and the payment's trace in Aspire running from `POST /api/v1/payments` into the worker's span (`docs/images/aspire-outbox-trace.png`, now in the README and the demo guide).
- Gate: locked restore, format, Release build, 382 backend tests green three runs in a row; `check-stack.sh` with no startup warnings; docs links.

## 2026-10-29: the webhook endpoint API and its SSRF rules (T23b)

- `backend: let merchants set a webhook endpoint, with SSRF rules on its URL`: no requirement row closes yet (R9 and R27 wait for delivery, T23c).
  - `GET` and `PUT /api/v1/webhook-endpoint` and `POST /api/v1/webhook-endpoint/rotate-secret` (`Api/Endpoints/WebhookEndpoints.cs`; `Application/Webhooks/SetWebhookEndpoint`, `GetWebhookEndpoint`, `RotateWebhookSecret`): one endpoint per merchant, the `whsec_` secret in the body only of the answer that issues it, `PUT` safe to repeat, and a first `PUT` that loses the race to create turned into an update of the winner.
  - `WebhookUrlPolicy` and `WebhookAddresses`: `https` only, no user info or fragment, and every address the host resolves to public (IANA special-purpose ranges, IPv4-mapped and NAT64 forms by the address they carry, Azure's `168.63.129.16`), with a 3 s resolve limit; `ResolveAllowedAsync` is ready for the connect-time check. `Webhooks:LocalReceiverHosts` frees named hosts for the compose receiver, and both hosts refuse to start with it outside Development.
  - `Persistence/DatabaseErrors.IsDuplicateKey`, shared with the idempotency store.
  - 117 new tests (96 unit, 21 integration); mutations that skipped the URL check, showed the secret on `GET`, dropped the race handling, logged the whole URL, or skipped the startup refusal each failed a test.
- Found on the way: six concurrent first `PUT`s never collided in practice, so the race branch was untested even with a gate holding all six until they had arrived; an EF `SaveChangesInterceptor` that inserts the rival row between the read and the save now makes the collision happen every time.
- Found on the way: the container build (SDK 10.0.4xx) failed on CA1873, an argument computed inside a log call, that the local SDK 10.0.101 does not report; the compose build is the gate that catches what CI's SDK would.
- Checked on the compose stack: the metadata address, `localhost`, `http://`, an unresolvable name, and `https://sqlserver/` (a container on the stack's private network) are each refused with their code; `https://example.com/...` through real DNS is accepted; rotation issues a 49-character secret; the API logs the host and never a `whsec_` value.

## 2026-10-29: signed webhook delivery (T23c)

- `backend: deliver webhooks, signed, with retries, dead letters, and a local receiver`: R9, R15, R21, and R27 are done.
  - `DeliverWebhooks` and the `deliver-webhooks` job (every second, runs untraced): a claim of 50 due deliveries under a one-minute lease (`UPDLOCK, READPAST, ROWLOCK`), 16 sent at a time, each outcome saved only while its lease stands (checked on read-back, and `LeasedUntil` is a concurrency token, migration `WebhookDeliveryLease`); `2xx` delivered, `410` dead-letters and disables the endpoint, a disabled endpoint's deliveries dead-lettered unsent, anything else retried on `WebhookRetrySchedule` (10 s doubling to 1 h, full jitter, 12 attempts, at most about 3.4 hours of waiting, which corrects ADR 003's "about a day").
  - Standard Webhooks signatures (`webhook-id`, `webhook-timestamp`, `webhook-signature: v1,...` over `id.timestamp.body`), checked against the specification's test vector; secrets moved to standard base64 so its libraries verify them. `X-Correlation-Id` and `traceparent` go with every delivery.
  - `HttpWebhookSender`: the connection callback resolves and connects only to addresses the SSRF rules allow (a name rebound to loopback after it was set reaches nothing), no proxy, redirects, or cookies, 5 s to connect and 10 s per attempt.
  - Compose: `webhook-receiver` (`scripts/webhook-receiver.mjs`) verifies signatures with the demo merchant's local-only secret, drops repeated ids, lists what it got at `:8090/events`, and fails on purpose for `webhook-fail` references; the seed points the demo merchant's endpoint at it; `check-stack.sh` waits for it.
  - 34 new tests (17 unit, 17 integration); mutations that ignored leases, dropped the lock hints, skipped the lease check or the lease token, kept a `410` endpoint enabled, allowed a 13th attempt, or sent to a disabled endpoint each failed a test.
- Found on the way: the T23b CI run failed once in `SigningKeyTests` with `ObjectDisposedException`: a `ValidateOnStart` failure through `WebApplicationFactory` races the failed `RunAsync` disposing the host. The test now starts a host with the API's authentication setup directly. Looping the suite at two threads to reproduce it found a second flake, in `OutboxTests`: "4242" also matched a timestamp's fraction of a second; it now checks the whole card number and the parsed event.
- Found on the way: the lock-hint and lease-token mutations passed the first tests (an `UPDATE` takes update locks anyway, and the read-to-save window is too short to hit by chance); a held lock and a `SaveChangesInterceptor` takeover now make each fail.
- Checked on a fresh compose volume: a payment's `payment.authorized` and `payment.captured` arrived verified about 1.5 s after each change, with the correlation id and `traceparent`; a `webhook-fail-1` payment's delivery was retried about 10, 20, and 40 s apart with `HTTP 503`; the capture's trace in Aspire runs from the API through `outbox dispatch` and `webhook deliver` to the `HTTP POST 204` (`docs/images/aspire-outbox-trace.png`, retaken).

## 2026-10-29: settlement through a stored procedure (T24)

- `backend: settle captured payments through a stored procedure`: R9's second workflow; the stack's "SQL with stored procedures" is now real.
  - `payments.SettleCapturedPayments` (migration `Settlement`, `Persistence/SettlementProcedure.cs`): a batch of payments captured before the delay, moved to `Settled` with `UPDLOCK, READPAST, ROWLOCK` over the new filtered index `IX_Payments_Captured`, their audit rows and `payment.settled` events written in the same transaction, the payload in exactly the C# outbox's shape; it returns count and amount per currency.
  - `Application/Settlement/SettlePayments` and the `settle-payments` job (every minute, traced, so each batch's webhooks join its trace); `Settlement:Delay` is a day by default and a minute in compose.
  - `MovePayment` re-reads a payment that changed between its read and its save and saves the move against the new state when it is still legal: settlement made a new concurrent writer, and a refund approved by the acquirer while the payment settled would otherwise have been refunded at the acquirer and refused in the database.
  - The simulated acquirer's spans now carry their tags from the start, so a listener or sampler sees them.
  - 7 new integration tests and the worker's job test widened; mutations that dropped the delay, settled authorized payments, dropped nulls or lowercase ids from the payload, skipped the audit rows, dropped `READPAST`, or answered the conflict without re-reading each failed a test.
- Found on the way: the idempotent migration script, the production deploy path, would not have applied. `CREATE PROCEDURE` cannot sit inside the script's `IF NOT EXISTS (...)` block, so the migration runs it through `EXEC`; and `sqlcmd` without `-I` failed on the T23a filtered indexes (`QUOTED_IDENTIFIER`). The full script now applies twice to an empty database.
- Found on the way: the first race test hung on the fake clock, because the simulated acquirer's latency runs on the injected `TimeProvider`; the test now waits for the refund's acquirer span, settles, and then advances the clock.
- Checked on the compose stack, upgraded from the T23c volume: the first run settled all 37 seeded captured payments in one batch (totals logged per currency) and their 37 webhooks arrived verified with the run's `traceparent`; `INV-1025`, captured live, settled 80 s later by `system` `settlement`, and its `payment.settled` webhook followed 2 s after.

## 2026-10-29: domain and queue metrics (T25)

- `backend: add payment and queue metrics` (checkpoint `e8d631c`, paused by the owner) and `backend: test and document the payment and queue metrics`: R19 and R20 are done.
  - `PaymentPlatform.Payments`: `payments.transitions` by `payment.status.from` and `payment.status.to`, counted by `PaymentTransitionInterceptor` once a save commits, only for audit rows `RecordChanges` marked as news (the seed's are not), plus settlement's count per batch, which the procedure writes past EF.
  - `PaymentPlatform.Outbox`: `webhook.delivery.attempts` by `webhook.outcome`, and gauges for outbox depth and oldest age, deliveries due and their lag, retries, dead letters (a new filtered index, migration `DeadLetterIndex`), and the settlement backlog, over a snapshot the worker's `measure-queues` job takes every 10 s.
  - 6 new integration tests with `MetricCollector` on each host's own meter factory; mutations that counted before the commit, counted the seed, dropped settlement's count, called a dead letter a retry, or reported zeros before the first measurement each failed a test.
- Found on the way: the "reports nothing before it measures" assertion first passed under its mutation, because the gauges' singleton had not been made yet; the test now makes it first.
- Found on the way: Aspire draws a counter's not-yet-exported interval as a fall to zero, which reads as a counter going backwards; the new `docs/images/aspire-webhook-metrics.png` shows the table instead.
- Checked on a fresh compose volume: every instrument reached the Aspire dashboard; delivery attempts rose from 5 to 42 when settlement's first run sent 37 `payment.settled` webhooks.

## 2026-10-29: dashboard sign-in and a nonce-based Content-Security-Policy (T26)

- `frontend: sign in to the dashboard, with a nonce-based Content-Security-Policy`: R10 is done.
  - `/login` (page, Server Action, form): the API called from the dashboard's server, the token kept in the `HttpOnly`, `Secure`, `SameSite=Strict` `pp_session` cookie for its lifetime, errors by `code`, the field at fault marked, the email kept, a return only to a path on this site; sign-out in the header, which names the merchant from `/me`.
  - `src/proxy.ts`: a nonce per page request and a policy with no `'unsafe-inline'` scripts, and a redirect to `/login?next=...` for a browser with no session; the root layout renders every page per request (ADR 009).
  - 42 new Vitest tests and 10 new Playwright checks against a stub API (`e2e/stub-api.mjs`); the existing shell checks sign in by cookie.
  - Compose shows the demo login on the sign-in page; `check-stack.sh` checks `/login`; the capture script signs in once and shares the session, and adds `docs/images/dashboard-login.png`.
- Decided on evidence: on the default Cache Components rendering, a nonce policy left 11 of 18 scripts without the request's nonce, the browser refused 9, and the page did not hydrate; with every page rendered per request, all 17 carry it and nothing is refused (ADR 009).
- Found on the way: at 360px the signed-in header overflowed by 4 px (the brand and "Sign out" wrapped); below `sm` the header now holds only the brand and the session, and buttons never wrap. The login page showed "Sign in" in the header for an ended session; the menu now stays empty there. An always-mounted alert left a 60 px gap under the password; it now takes no space while empty.
- Found on the way: the capture script's first run signed in six times for one email, and the API's per-email limit (5 in 5 minutes) refused the sixth, as designed.
- R16 is done: every log message was checked against ADR 005's policy. One was wrong: a webhook dead-lettered after its retries waits for an operator, so it is now `Error`; a dead letter the merchant caused (a `410`, a disabled endpoint) stays `Warning`, and a test holds both.

## 2026-10-29: the payments list (T27)

- `frontend: list and filter payments in the dashboard`: R11, R12, and O4 are done.
  - `/payments`: the merchant's payments with the session's token, newest first, 25 to a page with the API's cursor, a table from `lg` and cards below it, amounts in each currency's own form; filters for statuses, a UTC date range with the end day included, an amount range in a chosen currency (an amount without one is a field error, because JPY has no minor unit), a currency, and a reference prefix, in a `GET` form folded into a `<details>`; empty, error, and ended-session states; the header's sections beside the brand with `aria-current`, and a "View payments" action on the home page for phone widths.
  - 49 new Vitest tests and 8 new Playwright checks against the stub, which gains 30 fixed payments.
  - The capture script adds the list at desktop width and at phone width in the dark scheme, both taken on a fresh volume so they show the seeded payments.
- Found on the way: a `GET` form sends its empty fields, so the address read `?status=failed&from=&to=&reference=&currency=USD&min=&max=`; the page now redirects such a request to its clean address.
- Found on the way: during a database outage the home page's status stopped changing. Every refresh now waits on `/me`, which waits on the database, so a refresh took longer than the 5 s interval, and each one cancelled the last. Reproduced in the browser (the badge read "API ready" a minute into the outage); `LiveUpdates` now skips a tick while a refresh runs, and `/me` gives up after 3 s like the probes. The badge then turned within 10 s and recovered within 30.
- After the push: CI failed once in `OutboxTests` on "the work was shared, not done by one dispatcher": on the two-core runner one dispatcher drained all 250 events before the others started. That is not a guarantee of the system, so the assertion is gone from it and from `DeliveryTests`; the lock-hint mutation still fails both the concurrency test and the held-lock test.

## 2026-10-29: payment detail (T28)

- `frontend: open a payment, with its timeline, capture, and refund`: R13 is done.
  - `/payments/{id}`, opened from every row and card of the list: the amount, the status and what it means, the card with its expiry, the authorization reference, the failure reason in words; the history oldest first, each change with its two statuses, who made it (a merchant user, or the `settlement` job by name), the time in UTC, and its correlation id.
  - Capture and refund through one Server Action under an `Idempotency-Key` made for each render of the form, the refund behind a second step; each refusal's `code` in words, and the page refreshed when the payment had already moved on.
  - 26 new Vitest tests and 9 new Playwright checks against the stub, which gains one payment's history and the two moves, kept per token.
  - The capture script adds `docs/images/dashboard-payment-detail.png` (INV-1004, the whole lifecycle), taken on a fresh volume.
- Found on the way: signing in again from an ended session on a payment's page sent the browser straight back to sign in. Reproduced in the browser: the action redirected to the payment, then the router replayed the old redirect, because a redirect thrown by the page itself stays caught in its segment while the router keeps the left route mounted. Purging the client cache did not help; the read now runs behind its own `<Suspense>`, as the list's does, and a test signs in and lands on the payment.
- Found on the way: every page streams (ADR 009), so a payment that is not found answers `200` with `noindex` rather than `404`, as the Next.js docs describe; recorded in `docs/tradeoffs.md`.
- Found on the way: in two tabs, a capture in one left the other offering **Capture** under "can no longer be captured"; that refusal now refreshes the page. Long ids wrapped one character onto a second line in the narrow details column and at 360px, and opening the refund's confirmation pulled its button off the right edge; all three fixed and checked at 360 and 1440 in both schemes.
- Checked on the composed stack: INV-1025 captured and refunded from its page, with a correlation id on each new step and verified `payment.captured` and `payment.refunded` webhooks at the receiver; INV-1004's five steps with the settlement job; an unknown id; no console errors.

## 2026-10-29: the client's address, per person (T26b)

- `security: limit sign-ins per person, with the address the dashboard's edge saw`: ADR 010.
  - Compose's dashboard is reached only through a Caddy edge (`infra/edge/Caddyfile`, `caddy:2.10.2-alpine`), which writes the address it saw into `X-Forwarded-For` and drops `Server` and `Via`; App Service's front end does the same in Azure.
  - The dashboard takes the entry its `TRUSTED_PROXY_HOPS` proxies wrote (`src/lib/client-address.ts`, ports and brackets stripped) and names it to the API on sign-in in `X-Client-Address` with `X-Dashboard-Key`; the API believes it only with the right key, compared as hashes in fixed time, groups IPv6 by `/64`, and logs a warning for a named address without the key (`Api/Auth/ClientAddress.cs`).
  - The per-address limit is now 20 a minute, per person; Bicep gives the API and the dashboard the `dashboard-key` secret, the dashboard's role scoped to that one secret.
  - 10 new integration tests and 5 new Vitest tests; mutations that accepted any key or dropped the `/64` failed them.
- Found first, from the installed source and then on a build: Next.js keeps a browser's own `X-Forwarded-For` (`x-forwarded-for ??= socket.remoteAddress`); a forged `203.0.113.7` reached `src/proxy.ts` as sent. Network trust (`KnownNetworks`) was rejected because in Azure the dashboard reaches the API over the internet from shared addresses.
- Checked on the composed stack: 22 sign-ins from the host, each with a different forged `X-Forwarded-For`, were refused from the 21st; the same sign-in replayed from another address on the compose network got its own limit while the host's was spent; no key warnings.
- Not checked locally: the Bicep build and lint, because this machine has no Azure CLI and downloading the Bicep binary was refused by the session's permissions; CI's infra job runs them.
- T26b's operator view and banner moved to their own row, T26c.

## 2026-10-29: the golden path through the composed stack (T29)

- `test: walk the golden path through the composed stack in CI`: `frontend/e2e-stack/golden-path.spec.ts` with `playwright.stack.config.ts` (`npm run e2e:stack`), one test in steps against the real dashboard, API, SQL Server, worker, and receiver: sign in, INV-1004's five-step history with the settlement job, capture and refund of whichever payment is authorized (so a used volume passes too), the worker's signed `payment.captured` and `payment.refunded` at the receiver, not found, and sign-out.
  - CI's `stack` job runs it after `scripts/check-stack.sh`, with its report kept on failure.
  - Green three times running on the local stack; with the worker stopped it failed at the webhook step, as it should.
- The dashboard's OpenTelemetry, which T29 was to fold in, is its own row now (T29b), so this one stayed the golden path.

## 2026-10-29: traces that start at the dashboard's server (T29b)

- `frontend: trace the dashboard's server, joined to the API's traces`: `src/instrumentation.ts` with `@vercel/otel` 2.1.3 and its OpenTelemetry 2.x peers at exact versions (each at least two weeks old; `npm audit` clean), OTLP/HTTP to the Aspire dashboard's port 18890 in compose; `traceparent` only to `API_ORIGIN` and `WORKER_ORIGIN`; health probes left out.
- Checked on the composed stack after the golden path: the capture and the refund are each one trace of `payment-dashboard` (13 spans), `payment-api` (12), and `payment-worker` (5), from the Server Action to the webhook delivery; page renders join the API's `/me` and list calls.
- In Azure the dashboard needs an OTLP endpoint (a collector or Azure Monitor's OTLP ingestion); recorded in `infra/README.md`.

## 2026-10-29: the load test (T31)

- `test: load-test the composed stack with k6, and name the first bottleneck`: `scripts/load/payments.js` (k6 2.2.0, the `load` compose profile, never started by `docker compose up`), a constant arrival rate of checkouts (create, read, capture every second one, the newest page of the list) against the real API, judged by p95 thresholds; `docs/load-test.md` holds the method, the machine, and the results.
- Results on one machine (Docker Desktop, 32 CPUs): every threshold met from 20 to 600 checkouts a second (70 to 2,090 requests a second), no failed request; create's p95 from 120 ms (the acquirer's 80 ms plus the API's work) to 294 ms.
- The first bottleneck is the asynchronous path: at 600 a second the single worker's outbox and delivery queues grew for the whole minute, while it drained 13,800 deliveries in 14 s once the load stopped, because it shares SQL Server with the API. A second worker bounded the backlog at about 7,000 and cleared it within 10 s; SQL Server's CPU (6 to 7.5 cores) is the next limit.
- Found on the way: `docs/architecture.md` named the list's index `(MerchantId, CreatedAt, Id)`; it is `(MerchantId, CreatedAt, Sequence)`, fixed.

## 2026-10-29: the documentation checked against what was built (T30)

- `docs: check the tradeoffs, assumptions, production notes, and future improvements against the code`: each claim read against the code and the tests, not only the other docs.
- Found and fixed: `docs/architecture.md` said the worker reconciles payments stuck in `Pending`, which nothing does (queued as T41, and marked planned in the architecture, assumptions, and production pages); two sections still headed "planned" were built long ago; the webhook retries were described as lasting about a day, but the schedule's 12 attempts take at most about 3.4 hours; the integration tests were "about 20 seconds" and take about a minute; `future-improvements.md` still listed the dashboard's OpenTelemetry, a nonce-based CSP, and a parallel idempotency test, all built.
- Checked and still true: the event names (`payment.{status}` from `OutboxMessage`), the full-amount capture and refund, UTC everywhere, and the 30-minute login.

## 2026-10-29: reconcile payments left Pending (T41)

- `backend: reconcile payments the acquirer did not answer for`: `ReconcilePendingPayments` and the worker's `ReconcilePendingPaymentsJob` (every minute, traced): each payment `Pending` past a grace period (2 minutes, 30 s in compose) is authorized again with its id as the acquirer's idempotency key, and moved to `Authorized` or `Failed` with its audit row and event as the `system` actor `reconciliation`; one still unanswered is asked again after the retry interval (the shadow column `ReconcileAfter`, filtered index `IX_Payments_Pending`, migration `ReconcilePending`), and fails as `acquirer_unreachable`, logged at `Error`, after a day. Two workers may both ask; the row version records the change once. The gauge `reconciliation.payments.due` counts the queue.
  - The simulator's new test card `4000 0000 0000 0127` answers only once the payment is a minute old, read from its version 7 id, so it stays stateless; the seed's pending payments older than a day are now what reconciliation would have made them (INV-1023 and INV-1027 failed as `acquirer_unreachable`), so a fresh stack's first run changes nothing and logs no warning.
  - 7 new tests; mutations that ignored the retry interval, never gave up, or counted the next attempt from the acquirer's answer each failed one.
- Found on the composed stack, twice: counting the next attempt from the acquirer's answer, and then from the run's start, both let the next run miss the payment by milliseconds and leave it for the one after; the next attempt now counts from the run's start, less 5 s. Two late-answer payments then resolved in 97 and 107 s.
- Found by the container build, not the local one: `CA1873` on a computed log argument, which this machine's SDK 10.0.101 does not report and CI's 10.0.4xx does (the queued SDK update).
- A retried create that resumes a payment reconciliation has just finished gets a `500`, its key is released, and its own retry answers with the reconciled payment; handling it in `CreatePayment` was written and taken out again, because no test could make the race happen on demand.

## 2026-10-29: platform staff as their own identity in the API (T26c)

- The owner chose the model when asked: staff as an identity of their own, over an `operator` role on a merchant user or a reworded panel; ADR 011.
- `backend: give platform staff their own identity, sign-in, and the dead-letter replay`: `StaffUser` (`Domain/Staff/`, table `StaffUsers`, migration `StaffUsers`), `LogInStaff` and `GetCurrentStaff`, the shared `PasswordVerifier`, and `POST /api/v1/staff/auth/login` with the merchant login's limits under a `staff:` prefix; a staff token has `role` `staff` and no `merchant_id`.
  - Staff routes are a group of their own (`MapStaffApiV1`) behind the `staff` policy; merchant routes keep the default policy, so each kind is `403` on the other's routes, and a token claiming both is refused on the staff side.
  - `GET /api/v1/staff/webhook-deliveries?status=dead_lettered` (keyset paged, endpoint by host only) and `POST .../{id}/replay` (`WebhookDelivery.Replay`: back to `Pending`, attempts from zero, due now; `409` unless dead-lettered and its endpoint enabled), logged with the staff id and counted as `webhook.delivery.replays`.
  - Compose seeds `operator@example.com` beside the demo merchant.
  - 20 new integration tests; mutations that let a token claiming a merchant pass the staff policy, or that passed every token, failed them.
- Checked on the composed stack: the staff login, `403` both ways, a delivery dead-lettered by hand listed with its host, replayed, and delivered by the worker within a second, and the replay's log line.
- Found on the way: `sqlcmd` writes to a table with filtered indexes only with `-I`; the demo's command carries it.

## 2026-10-29: the operator area in the dashboard (T26d)

- `frontend: give platform staff an operator area, and merchants one line about the platform`: `/operator/login` (the merchant login's form, through `signInFromForm`, against the staff route) with `pp_staff_session` on `/operator` only; `/operator` with the service status panel moved from the merchant's home page and the dead letters with **Replay**; the header marks the area and shows the API badge there only; `src/proxy.ts` keeps each area to its own session.
  - A merchant's home page shows **Payments are delayed** when the API is not ready, and nothing of the database or the worker; the payments error page no longer points merchants at a status they cannot see.
  - `scripts/check-stack.sh` checks the API badge on `/operator/login`; the golden path signs in as the operator too; the capture script signs in as each kind and makes `operator-*.png` and `dashboard-home.png` in place of the old home-page images, and the outage from the operator area.
  - 19 new Vitest tests and 8 Playwright checks; the stub answers the staff routes.
- Found on the way: a query on the operator area's home (`/operator?tab=x`) lost its query on the way to sign-in; the proxy now keeps `next` whenever there is a query.
- Found while capturing the outage: with the database stopped, the failed read of the dead letters took the whole operator page to its error boundary, status panel and all; the list now reports its own failure in place, and a test with the list down holds the panel on screen.
- Found while testing: Playwright's `addCookies` with a URL puts a cookie on `/` whatever the URL's path, so the test's staff cookie survived sign-out; the helper sets the path as the server does.
- At 360px the header's **Operator** badge collided with **Sign out**, and the dead letters' ids wrapped; the badge shows from `sm` up, and each id sits under its label at phone width.

## 2026-10-29: the template database, measured and dropped (T42)

- The row assumed each test class spent about 3.5 s migrating its database. Measured: a migration takes about 0.8 s, and a restore of a migrated template's backup about 0.6 s; with the template in place the integration suite took 1 min 15 s, against 1 min 14 s and 1 min 16 s without it.
- The 0.2 s saved per test disappears across the parallel run, so the change was reverted rather than kept for its complexity; the 3.5 s figure in `notes/present.md` and `docs/tradeoffs.md` is corrected. (Database snapshots were ruled out first: they are read-only.)
- Queue close-out: no workable row is left. The two rows waiting on the owner (the local SDK update and the Dependabot pull requests) and the parked rows stay in `future.md` with their conditions.

## 2026-10-29: the SDK and the dependency pull requests (owner approved)

- This machine's .NET SDK went from 10.0.101 to 10.0.403 (winget), the band CI uses, so local builds now report what CI's analyzers report; the Release build and `dotnet format` were clean on it.
- Dependabot had already closed #1 to #5: the four majors are held back by the ignore rules, and #6 replaced #2. #6 failed `npm ci`: it moved `@opentelemetry/api` to 1.10 and `@opentelemetry/instrumentation` to 0.300 but not the rest of the set, which `@opentelemetry/resources` 2.12 and every `@vercel/otel` release up to 2.1.4 refuse.
  - Its safe half, React and React DOM 19.3.1 (released 2026-10-14), is on `main` with every frontend gate green.
  - `.github/dependabot.yml` now groups `@opentelemetry/*` with `@vercel/otel`, kept out of the general npm group, so tracing packages only move together; #6 was closed with the reason.
