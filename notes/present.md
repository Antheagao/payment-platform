# Present

A snapshot of where the project stands right now.
Rewrite this file rather than appending to it; history lives in `past.md`, plans in `future.md`.

Last updated: 2026-10-28.

## The brief (2026-10-28)

The repository answers a software engineer technical exercise: a simplified payment processing platform with a backend API, a merchant dashboard, and at least one asynchronous workflow.
The exercise grades system design, code quality, architectural decisions, operational thinking, and tradeoffs; it prefers a small, well-reasoned slice over a broad, shallow one.
The requirements are restated in our own words in `docs/requirements.md`, each traced to where it lives and its status.

The base of the project was requested with these requirements; they are recorded here so nothing drifts:

1. Match the hiring team's Microsoft stack: C# and .NET for APIs, services, and integration layers; Azure (Functions, App Service, Logic Apps, Storage, Azure SQL, monitoring); SQL with stored procedures; Python for automation only if a data pipeline needs it.
   Preferred extras they list: Azure DevOps CI/CD, Terraform or Bicep, event-driven messaging, ETL.
2. Frontend: Next.js, TypeScript, and Tailwind CSS.
3. A `frontend/` folder and a `backend/` folder at the root.
4. A `.claude/` folder with `CLAUDE.md` plus testing, CI/CD, security, architecture, and design guides drawn from the claude-brain repository.
5. Built through the batch loop, with a commit and a push after each task or every few tasks, so the history shows the progress.
6. Database: SQL Server (the owner's choice on 2026-10-28, over the usual PostgreSQL default, to match Azure SQL).
7. A public GitHub repository.

## What exists

- **Repository** `C:\projects\payment-platform` on `main`, pushed to `github.com/Antheagao/payment-platform` (public).
  Layout so far: `.claude/`, `.githooks/`, `docs/`, `notes/`, `scripts/`, `AGENTS.md`, `README.md`, `.editorconfig`, `.gitattributes`, `.gitignore`.
- **Decisions** in `docs/decisions/`: 001 stack and layout, 002 backend project boundaries, 003 the async workflow on a transactional outbox, 004 short-lived JWTs held by the dashboard's server, 005 OpenTelemetry, 006 RFC 9457 problem details with a stable `code`, 007 idempotency keys.
  `docs/assumptions.md` records the fifteen choices made where the brief was ambiguous.
- **Architecture**: `docs/architecture.md` (components, project dependencies, the lifecycle, the create and async sequences, observability, security, scaling, the Azure mapping, with Mermaid diagrams) and `docs/requirements.md` (27 requirements and 6 documentation deliverables, each with its home, task, and status).
- **Docs gate**: `node scripts/check-docs.mjs` fails on a broken relative link or an em dash in any Markdown file.
- **backend/** (no projects yet): `PaymentPlatform.slnx`, `global.json` (SDK 10.0.100 with `latestFeature`; this machine has 10.0.101), `Directory.Build.props` (net10.0, nullable, warnings as errors, `latest-recommended` analyzers, code style in build, NuGet audit of transitive packages, lock files restored in locked mode under `CI=true`), `Directory.Packages.props` (central versions with transitive pinning), `BannedSymbols.txt` (wall-clock time, `Console.WriteLine`, blocking waits, `new Random()`), and `dotnet-ef` 10.0.14 in the tool manifest.
  C# style rules live in the root `.editorconfig`.
- **Notes rotation**: `.githooks/pre-commit` refuses a commit that changes anything outside `notes/` without staging `notes/present.md`; `.githooks/pre-push` refuses a push range that never touches it.
  Activate once per clone with `git config core.hooksPath .githooks`.
- **Agent rules**: `AGENTS.md` (always loaded; imported by `.claude/CLAUDE.md`) holds the orientation, gates, guide table, and commit rules.
  `.claude/skills/batch-loop` maps the claude-brain `campaign-loop` onto these notes; `payment-platform-pull-requests` is the first guide overlay, and the rest land with the code they describe.

## Task board

| Task | What | Status |
| --- | --- | --- |
| T1 | Repository base, notes rotation, agent rules | Done |
| T2 | Architecture overview, requirements traceability, ADRs | Done |
| T3 | Backend solution, SDK pin, shared build and package props | In progress: conventions landed, overlays next |
| T4 | Domain: payment lifecycle state machine with unit tests | Queued |
| T5 | Service defaults: JSON logs, OpenTelemetry, health, request id | Queued |
| T6 | Persistence: EF Core on SQL Server, readiness check | Queued |
| T7 | Application layer: use-case home, acquirer port and simulated adapter | Queued |
| T8 | API host: `/api/v1`, problem details, OpenAPI, health endpoints | Queued |
| T9 | Worker host: periodic job runner, health endpoints | Queued |
| T10 | Integration tests on a real SQL Server (Testcontainers) | Queued |
| T11 | Architecture tests for project dependency direction | Queued |
| T12 | Dockerfiles and `docker-compose.yml` with SQL Server and the Aspire dashboard | Queued |
| T13 | Frontend scaffold: Next.js 16, TypeScript, Tailwind v4 | Queued |
| T14 | Frontend shell: design tokens, layout, API status | Queued |
| T15 | Frontend tests: Vitest and a Playwright smoke test | Queued |
| T16 | CI: GitHub Actions, Dependabot, CodeQL | Queued |
| T17 | Infrastructure as code: Bicep for Azure, validated in CI, never deployed | Queued |
| T18 | README and documentation deliverables | Queued |

Feature work (T19 onward) is ranked in `future.md`.

## How it runs

Nothing runs yet; T12 adds `docker compose up`.

## Decisions pending

None.
