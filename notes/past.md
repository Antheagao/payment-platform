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
