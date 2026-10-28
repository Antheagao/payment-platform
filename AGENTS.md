# Payment Platform agent rules

Project rules on top of the global ones in `~/.claude/AGENTS.md` (from the claude-brain repository).
Keep this file small: it loads on every session in this repository.
Longer procedures live in `.claude/skills/`.

## What this is

- A technical exercise: a simplified payment processing platform with a C# / .NET 10 API, a background worker, SQL Server, and a Next.js merchant dashboard.
  It is graded on system design, code quality, architecture, operational maturity, and the tradeoffs written down, so depth beats breadth.
- It runs locally only.
  `infra/` holds Bicep that is validated in CI and never deployed.
- The repository is public: never commit a secret, a real card number, or anything from `.env`.

## Orientation

- Read `notes/present.md` first; it is the current state, the task board, and how things run.
- `notes/future.md` is the ranked roadmap and the queue for the `batch-loop` skill; `notes/past.md` is the log.
- Decisions live in `docs/decisions/`; the system shape lives in `docs/architecture.md`.
- `frontend/AGENTS.md` is written by `next dev`: Next.js 16 differs from training data, so read `frontend/node_modules/next/dist/docs/` before writing frontend code.

## Running it

- Whole stack: `cp .env.example .env` once, then `docker compose up --build` (SQL Server, the database bootstrap, the Aspire dashboard, the API, the worker); `docker compose down` to stop.
- Inner loop: `docker compose up -d sqlserver sqlserver-init aspire-dashboard`, then `dotnet run` the API or worker from `backend/` with `ConnectionStrings__Payments` set.
- Docker Desktop must be running for the integration tests (Testcontainers) and for `docker compose`.
- Ports: API `5080`, worker `5081`, dashboard `3000`, SQL Server `14333` on the host, Aspire dashboard UI `18888`, OTLP `4317`.
  Stop every server, container, and watcher you start before finishing.

## Gates (the same commands CI runs)

- Backend, from `backend/`: `dotnet format --verify-no-changes`, `dotnet build -c Release`, `dotnet test -c Release`.
  Warnings are errors and NuGet audit runs on restore, so a vulnerable package fails the build.
- Frontend, from `frontend/`: `npm run typecheck` (typegen, then `tsc`), `npm run lint` (zero warnings), `npm run format:check`, `npm test` (from T15), `npm run build`, `npm audit --omit=dev`.
- UI work is not done until it has been checked in a real browser, light and dark, 360px to 1440px.
- Write long build and test output to a scratch file and grep it rather than printing it into the transcript.

## Guides

The engineering rules are the global guides from the claude-brain repository (`~/.claude/skills/<topic>`).
Those guides are written for Rust, PostgreSQL, and Next.js; this repository adds a `payment-platform-<topic>` overlay for each that says what exists here, translates the stack-specific rules to C#, EF Core, and SQL Server, and records local exceptions.
Load the overlay first, then the global guide; the overlay wins on conflict.
An overlay lands with the code it describes, so a row whose overlay does not exist yet uses the global guide alone.

| Work | Global guide | Overlay |
| --- | --- | --- |
| Where a file goes, how it is named | `repository-structure` | `payment-platform-repository-structure` |
| System shape, where new features go, configuration | (none) | `payment-platform-architecture` |
| Projects, types, ports, ADRs | `design-principles` | `payment-platform-design-principles` |
| Writing or refactoring any code | `clean-code` | `payment-platform-clean-code` |
| XML doc comments, migration headers, TODOs | `code-comments` | `payment-platform-code-comments` |
| Input, auth, card data, secrets, headers | `security` | `payment-platform-security` |
| Endpoints and JSON contracts | `api-design` | `payment-platform-api-design` |
| Migrations, T-SQL, stored procedures | `database` | `payment-platform-database` |
| Errors, logs, metrics, traces | `error-handling-logging` | `payment-platform-error-handling-logging` |
| Tests and flaky CI | `testing` | `payment-platform-testing` |
| Pipelines, containers, infrastructure as code | (none) | `payment-platform-ci-cd` |
| Pages, components, styles | `frontend-ui` | `payment-platform-frontend-ui` |
| Commits and pushes | `pull-requests` | `payment-platform-pull-requests` |
| Working the roadmap | `campaign-loop` | `batch-loop` |

## Commits

- Every commit that touches code or docs also rotates the notes: landed work into `notes/past.md` with the commit named, `notes/present.md` rewritten to the new state, `notes/future.md` re-ranked.
  The `.githooks/` guards enforce it; activate them once per clone with `git config core.hooksPath .githooks`.
- Stage exact paths, never `git add -A`.
- Subjects are `<area>: <what landed>`, details in `payment-platform-pull-requests`.
