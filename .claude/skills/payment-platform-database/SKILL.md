---
name: payment-platform-database
description: Payment Platform SQL Server and EF Core rules - the global PostgreSQL guide translated to T-SQL, migrations, naming, types, locking, stored procedures, and what exists. Use when adding a table, migration, query, index, or stored procedure in this repository.
---

# Payment Platform database overlay

The global `database` guide is written for PostgreSQL and sqlx; its principles hold here (expand then contract, constraints mirror validation, bind every value, unique tiebreakers, short transactions), and this file translates the specifics to SQL Server 2025, Azure SQL, and EF Core 10.

## What exists

- `PaymentPlatform.Persistence`: `PaymentsDbContext` (default schema `payments`; no entities yet, so it does not scan for configurations, which EF Core warns about on every start when there are none; T19 adds `ApplyConfigurationsFromAssembly` with the first `IEntityTypeConfiguration`), `AddPersistence()` (requires `ConnectionStrings:Payments`, pooled contexts, transient-fault retries, 30 s command timeout, migration history in `payments.__EFMigrationsHistory`), and the `database` readiness check (connects and has no pending migration; its own context options with no retries and 2 s connect and command timeouts; run on the thread pool and abandoned at its 3 s deadline, because SqlClient resolves host names synchronously inside `OpenAsync`).
- No tables or migrations yet; the first lands with T19.

## Migrations

- `dotnet ef migrations add <PascalCaseName> --project src/PaymentPlatform.Persistence --startup-project src/PaymentPlatform.Persistence` from `backend/`; the design-time factory needs no database.
- Review the generated SQL before committing: `dotnet ef migrations script <previous> <new>`.
- Never edit a pushed migration; add a new one.
- Locally the API applies migrations at startup when `Database:MigrateOnStartup` is true (compose sets it; `DatabaseMigrator`); the worker never migrates. In a pipeline they run as an idempotent script (`migrations script --idempotent`) or a migration bundle before the new build takes traffic, which the readiness check enforces.
- The local database and login come from `scripts/sql/create-local-database.sql` (run by compose's `sqlserver-init`); it creates no tables.
- `ALTER DATABASE` and other statements that cannot run in a transaction use `migrationBuilder.Sql(..., suppressTransaction: true)`, one per migration.

## Safe under load (translations)

- Add columns as nullable, or `NOT NULL` with a constant default (metadata-only in SQL Server).
- New `CHECK` or foreign key on a large table: add `WITH NOCHECK`, then `ALTER TABLE ... WITH CHECK CHECK CONSTRAINT` later.
- Indexes on large tables: `WITH (ONLINE = ON)` (Azure SQL and Enterprise); `SET LOCK_TIMEOUT` in risky migrations.

## Schema rules

- Tables are plural PascalCase (`Payments`, `PaymentEvents`), columns PascalCase, matching the C# names (a local exception to the global snake_case: SQL Server and EF Core convention, and JSON keeps snake_case).
- Public ids are `uniqueidentifier` (GUID v7) as a nonclustered primary key; cluster each table on its dominant access path (for `Payments`, `(MerchantId, CreatedAt, Id)`), because SQL Server does not sort GUIDs by their time prefix.
- Time is `datetimeoffset` written in UTC; money is `bigint` minor units plus `char(3)` currency; never `money`, `float`, or `decimal` for stored amounts.
- Strings are `nvarchar(n)` with `n` equal to the API limit, or `varchar(n)` for ASCII-only codes; `nvarchar(max)` only for payloads.
- Closed sets (statuses) are stored as `varchar` names with a `CHECK` listing them, never as integers, so the data reads on its own and adding a value is a constraint swap.
- The default collation is case-insensitive, which suits emails; values that must compare exactly (idempotency keys, tokens, signatures) get `Latin1_General_100_BIN2`.
- Every merchant-owned row has `MerchantId`, and every index serving a merchant query leads with it.
- Optimistic concurrency on rows edited in place: a `rowversion` column mapped as a concurrency token; a conflict becomes `409`.

## Queries and locking

- LINQ or `FromSql`/`ExecuteSql` with interpolation (parameterized); never `FromSqlRaw` with concatenated input.
- Reads use `AsNoTracking()` and project to the response shape; every list has `Take(n)` and an `OrderBy` ending in `Id`.
- Queue claims use `UPDATE TOP (@n) ... WITH (UPDLOCK, READPAST, ROWLOCK) OUTPUT inserted.*`, the SQL Server form of `FOR UPDATE SKIP LOCKED`.
- Azure SQL runs read committed snapshot isolation by default and a SQL Server container does not, so the first migration turns `READ_COMMITTED_SNAPSHOT` on to keep local behavior identical.
- Retries are on, so a use case that opens a transaction runs it inside `Database.CreateExecutionStrategy().ExecuteAsync(...)`; never hold one across an acquirer or webhook call.
- Map `SqlException` numbers 2627 and 2601 (unique) to conflicts and 547 (foreign key or check) to not found or bad input; 1205 (deadlock) is retried by the strategy.

## Stored procedures

- One file per object in `Persistence/Sql/`, starting with `SET NOCOUNT ON; SET XACT_ABORT ON;`, set-based, batched with `TOP (@BatchSize)`, applied by a migration that reads the embedded file.
- Called with `Database.ExecuteSqlAsync($"EXEC payments.SettleDuePayments @BatchSize = {batchSize}")`.

## Retention

- Idempotency keys 24 hours; delivered outbox rows and webhook deliveries 7 days; dead letters until replayed or resolved; payments and their audit rows are never deleted.
