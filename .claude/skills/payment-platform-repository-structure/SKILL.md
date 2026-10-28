---
name: payment-platform-repository-structure
description: Payment Platform folder layout and file naming for the .NET solution, EF Core migrations, T-SQL, Bicep, docs, and scripts. Use when creating, moving, renaming, or splitting a file or folder in this repository.
---

# Payment Platform repository-structure overlay

Load the global `repository-structure` guide for the rules; its Next.js section applies to `frontend/` unchanged.
This file translates the Rust and PostgreSQL sections to .NET and SQL Server and records the local layout.
Top-level folders are fixed by ADR 001 (`docs/decisions/001-stack-and-repository-layout.md`); a new one needs a new ADR.

## The solution (`backend/`)

- `PaymentPlatform.slnx` at `backend/`, with solution folders `/src/` and `/tests/`.
- Shared settings live only in `Directory.Build.props` (framework, nullable, warnings as errors, analyzers, NuGet audit, lock files); every package version lives only in `Directory.Packages.props`.
  A project file adds references and what is specific to it, never a version or a shared property.
- `global.json` pins the SDK band; `.config/dotnet-tools.json` pins `dotnet-ef`; `BannedSymbols.txt` lists the APIs the analyzer refuses.
- Source projects: `src/PaymentPlatform.<Part>/`, where the folder, the assembly, and the root namespace are the same name.
  The parts and what may reference what are in ADR 002.
- Test projects: `tests/PaymentPlatform.<Part>.Tests/` for unit tests of one project, `tests/PaymentPlatform.IntegrationTests/` for everything that needs SQL Server or the HTTP pipeline, `tests/PaymentPlatform.ArchitectureTests/` for dependency rules.

## Files and namespaces

- One public type per file, named for the type (`PaymentStatus.cs`); namespaces mirror folders, file-scoped.
- The exception is a use case in `Application`: one file named for the verb phrase (`Payments/CapturePayment.cs`) holds its input record, its result, and its handler, because they always change together.
- Feature folders, not layer folders: `Application/Payments/`, `Application/Webhooks/`, `Application/Settlement/`; never `Services/`, `Helpers/`, `Utils/`, or `Common/`.
- API endpoints: one file per resource in `Api/Endpoints/` (`PaymentsEndpoints.cs`) that maps its route group.
- Worker jobs: one file per job in `Worker/Jobs/` (`DeliverWebhooksJob.cs`).

## Database

- EF Core entity configuration: `Persistence/Configurations/<Entity>Configuration.cs`, one per entity.
- Migrations: `Persistence/Migrations/`, created only with `dotnet ef migrations add <PascalCaseName>` from `backend/`.
  EF names files `<timestamp>_<Name>.cs`; that replaces the global `NNNN_` numbering here, and the same rule holds: never edit, rename, or delete a migration once it is pushed; fix forward.
- T-SQL objects (stored procedures, views) live as `Persistence/Sql/<schema>.<Name>.sql` and are applied by a migration that reads the embedded file, so the procedure's history is reviewable as SQL, not as a C# string.

## Everything else

- Bicep: `infra/bicep/main.bicep` plus one module per resource family in `infra/bicep/modules/`.
- Docs: ADRs as `docs/decisions/NNN-kebab-slug.md`; other docs kebab-case in `docs/`.
- Scripts: Node (`.mjs`) so they run on Windows, macOS, and Linux, kebab-case and verb first, in `scripts/` (`check-docs.mjs`).
