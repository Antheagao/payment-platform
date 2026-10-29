---
name: payment-platform-database
description: Payment Platform SQL Server and EF Core rules - the global PostgreSQL guide translated to T-SQL, migrations, naming, types, locking, stored procedures, and what exists. Use when adding a table, migration, query, index, or stored procedure in this repository.
---

# Payment Platform database overlay

The global `database` guide is written for PostgreSQL and sqlx; its principles hold here (expand then contract, constraints mirror validation, bind every value, unique tiebreakers, short transactions), and this file translates the specifics to SQL Server 2025, Azure SQL, and EF Core 10.

## What exists

- `PaymentPlatform.Persistence`: `PaymentsDbContext` (default schema `payments`; `ApplyConfigurationsFromAssembly` picks up `Configurations/`), `AddPersistence()` (requires `ConnectionStrings:Payments`, pooled contexts, transient-fault retries, 30 s command timeout, migration history in `payments.__EFMigrationsHistory`), and the `database` readiness check (connects and has no pending migration; its own context options with no retries and 2 s connect and command timeouts; run on the thread pool and abandoned at its 3 s deadline, because SqlClient resolves host names synchronously inside `OpenAsync`).
- `Payments`: nonclustered primary key on `Id`, unique clustered index on `(MerchantId, CreatedAt, Sequence)`, where `Sequence` is a shadow `bigint` identity column (`PaymentsDbContext.PaymentSequence`) that orders payments created at the same instant; it replaced `Id` as the tiebreak because SQL Server orders `uniqueidentifier` differently from .NET and EF cannot compare a converted id, and because EF Core 10 names complex-type columns by their defaults when a query composes over `FromSql`, so the keyset could not be raw SQL either. Two more indexes serve the list's filters: `(MerchantId, Status, CreatedAt, Sequence)` and `(MerchantId, MerchantReference)`. Checked with `SHOWPLAN_TEXT` on the 280 seeded payments: the default list, a status filter, and a reference prefix all seek the clustered index on `MerchantId`, ordered backward, and stop at `TOP`; the filter indexes start winning once a status or reference is rare among many rows; `Status` and `CardBrand` as `varchar` names with `CHECK`s generated from the enums (`StatusCheck`); `AmountMinor bigint` with a `CHECK` for the domain's range and `Currency char(3)` (the `Money` complex property); the card as `CardToken`, `CardBrand`, `CardLast4 char(4)`, `CardExpiryMonth tinyint`, `CardExpiryYear smallint` (the `PaymentCard` complex property); `RowVersion` as a shadow concurrency token the domain never sees.
- `PaymentEvents`: `bigint` identity key (append-only), index `(PaymentId, Id)`, statuses and actor kind with `CHECK`s, `CorrelationId varchar(64)`. `PaymentsDbContext.RecordChanges(payment, correlationId)` turns `Payment.DequeueEvents()` into rows for the next save, and both `SaveChanges` overloads throw if a tracked payment still has unrecorded changes.
- `IdempotencyKeys`: primary key `(MerchantId, Key)` (the lock), `Key varchar(255)` in `Latin1_General_100_BIN2`, `Fingerprint binary(32)`, status with a `CHECK`, the `PaymentId` it created, the stored response, `ClaimedAt` (a claim older than 30 s is abandoned), `ExpiresAt` (indexed for the cleanup), and a `RowVersion` that settles racing takeovers; a duplicate insert is recognized by SQL error 2627 or 2601.
- `OutboxMessages` (ADR 003): `bigint` identity key, so the queue is in commit order; unique `EventId`; `EventType varchar(40)`, `Payload nvarchar(max)` (the webhook body, no card data), `CorrelationId`, `TraceParent varchar(55)`, `OccurredAt`, `DispatchedAt`; the filtered index `IX_OutboxMessages_Undispatched` (`Id` where `DispatchedAt IS NULL`) is the queue the dispatcher reads with `UPDLOCK, READPAST, ROWLOCK`, so its claim never scans the history. `RecordChanges(payment, correlationId, publish)` adds one per status change after creation; the seed passes `publish: false`.
- `WebhookEndpoints` (one per merchant, unique `MerchantId`; `Url nvarchar(2048)`, `Secret varchar(64)`, `Enabled`) and `WebhookDeliveries` (`bigint` identity; unique `(OutboxMessageId, EndpointId)`, so a retried dispatch can never deliver one event twice; `Status` with a `CHECK`; `Attempts`, `NextAttemptAt`, `LeasedUntil`, `LastStatusCode`, `LastError nvarchar(500)`, `DeliveredAt`; the filtered index `IX_WebhookDeliveries_Due` on `NextAttemptAt` where `Status = 'Pending'`).
- Tables: `Merchants` (clustered on `Id`: a handful of rows written once) and `MerchantUsers` (unique `Email` under the case-insensitive collation, `PasswordHash varchar(256)`, a restricting foreign key to `Merchants`).
- Migrations: `InitialSchema` (merchants and users), `EnableReadCommittedSnapshot` (its only operation, outside a transaction, because EF Core warns when a migration mixes the two), `Payments`, `IdempotencyKeys`, `PaymentListKeyset` (which rebuilds the clustered index: fine locally, and in production a rebuild to schedule, or to do `ONLINE` where the tier allows), and `Outbox`; `Migrations/.editorconfig` marks them `generated_code` (it lives beside them because the container build's context is `backend/`, which the root `.editorconfig` is outside of) and the sealed-class architecture rule skips them.
- EF Core maps a get-only property only when a configuration names it, and leaves any other one out of the table without a warning, so its value is lost on save; `Startup/ModelMappingTests` fails when a public property of a stored type has no column (it caught three `CreatedAt`-style columns missing from the first `Outbox` migration).
- The test fixture migrates with the API's own provider settings (`PersistenceServiceCollectionExtensions.Configure`), so the history lands in `payments.__EFMigrationsHistory` where readiness looks for it; with plain `UseSqlServer` it went to `dbo` and every test saw a pending migration.

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
- Public ids are `uniqueidentifier` (GUID v7) as a nonclustered primary key; cluster each table on its dominant access path (for `Payments`, `(MerchantId, CreatedAt, Sequence)`), because SQL Server does not sort GUIDs by their time prefix.
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
