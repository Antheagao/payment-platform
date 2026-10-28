# 001. Stack and repository layout

- Status: accepted
- Date: 2026-10-28

## Context

The platform answers a technical exercise for a team that runs a Microsoft stack: C# and .NET for APIs and services, Azure (App Service, Functions, Logic Apps, Storage, Azure SQL, monitoring), SQL with stored procedures, and Python for automation.
The owner asked for a Next.js, TypeScript, and Tailwind frontend, a `frontend/` and a `backend/` folder at the root, and Python only where a data pipeline needs it.
Reviewers will run it on their own machines, so the setup has to be short and predictable.

## Decision

- **Backend:** C# on .NET 10 (the current long-term support release, supported until November 2028), ASP.NET Core minimal APIs, EF Core 10 on the SQL Server provider.
- **Database:** SQL Server 2025 in a container locally, the engine generation closest to Azure SQL Database; Azure SQL Database in the Azure design.
- **Frontend:** Next.js 16 with the App Router, React 19, TypeScript in strict mode, and Tailwind CSS v4.
- **Python:** none until a pipeline needs it (a reconciliation export is parked in `notes/future.md` with that condition).
- **Local orchestration:** one `docker-compose.yml` that starts SQL Server, the Aspire dashboard as the telemetry sink, the API, the worker, and the dashboard.
- **Layout:** one repository with these top-level folders, and no others without a new decision record:

| Folder | Holds |
| --- | --- |
| `backend/` | The .NET solution: `src/` projects and `tests/` projects |
| `frontend/` | The Next.js dashboard |
| `infra/` | Bicep for the Azure shape and its post-deployment SQL, validated in CI and never deployed |
| `docs/` | Architecture, requirements, assumptions, and these decision records |
| `notes/` | The rotating past, present, and future notes that drive the batch loop |
| `scripts/` | Developer and CI scripts, such as the documentation link check |
| `.claude/` | Agent configuration: `CLAUDE.md` and the guide overlays |
| `.github/` | CI workflows and Dependabot |
| `.githooks/` | The notes rotation guards |

## Rejected alternatives

- **PostgreSQL**, the owner's usual default: it would serve the domain equally well, but the team runs Azure SQL, and stored procedures in T-SQL are part of what this exercise shows.
- **A .NET Aspire AppHost for local orchestration:** it gives one-command startup with service discovery, but it adds an SDK requirement for anyone who only wants to run the stack.
  We keep the Aspire dashboard (as a standalone container) for the telemetry view and let compose do the orchestration.
- **MVC controllers:** they would work; minimal APIs with route groups and endpoint filters are the current default and carry less ceremony for a small surface.
- **Separate repositories for the API and the dashboard:** a change to an API contract and its client would land in two places; one repository lets it land in one commit.

## Consequences

- `docker compose up` needs only Docker; the development loop needs the .NET 10 SDK, Node 22, and Docker.
- The SQL Server image is built for x64; on Apple silicon it runs under Docker's Rosetta emulation.
- Every top-level folder has one purpose, so a reviewer can guess where anything lives.
