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
