# Tradeoffs

What we chose, what we gave up, and when we would choose differently.
The decision records in [decisions/](decisions/README.md) carry the full reasoning for the larger choices; this page is the short version, plus the smaller choices that did not need a record.

## Architecture

| Choice | What we gave up | We would revisit when |
| --- | --- | --- |
| **A transactional outbox polled from SQL Server** for the async workflow ([ADR 003](decisions/003-async-workflow-on-a-transactional-outbox.md)) | Up to a second of latency, and the database carries the queue load | Event volume outgrows one database; the outbox then relays to Azure Service Bus, which the Bicep already provisions |
| **Six backend projects** with dependencies pointing inward ([ADR 002](decisions/002-backend-project-boundaries.md)) | More projects than the smallest layout | Never fewer than the boundaries need; an architecture test fails if one is crossed |
| **No repository layer over EF Core** | A seam for unit-testing use cases without a database | Never for this team size: integration tests run on real SQL Server in about 20 seconds |
| **A deterministic, stateless simulated acquirer** | Realistic network behavior (partial failures, slow responses beyond a fixed latency) | A real acquirer sandbox becomes available; it is one adapter behind `IAcquirer` |
| **Authorization inside the create request** ([assumption 7](assumptions.md)) | Create latency includes the acquirer round trip | Acquirer latency hurts the merchant's checkout; then create returns `pending` and authorization moves to the worker |
| **One database, tenancy by `MerchantId`** | Hard isolation between merchants | A merchant's contract or regulator requires physical isolation |

## Interfaces

| Choice | What we gave up | We would revisit when |
| --- | --- | --- |
| **RFC 9457 problem details** with a stable `code` ([ADR 006](decisions/006-errors-as-problem-details.md)) | The smaller `{"error": {"code", "message"}}` envelope from the house guides | Not expected: merchant tooling already understands problem details |
| **Required `Idempotency-Key`** on money-moving calls ([ADR 007](decisions/007-idempotency-keys.md)) | A little convenience for callers who never retry | Never: a missing key is a double charge waiting to happen |
| **snake_case JSON, `amount_minor` integers** | The camelCase default of ASP.NET Core | Never; it is the payments-industry convention and the dashboard's own |
| **Keyset pagination with opaque cursors** | Jumping to page N | A merchant needs random access; then a search index serves it, not `OFFSET` |

## Security

| Choice | What we gave up | We would revisit when |
| --- | --- | --- |
| **30-minute JWTs, no refresh, held by the dashboard's server** ([ADR 004](decisions/004-authentication-with-short-lived-jwts.md)) | Instant revocation and long sessions | Revocation becomes a requirement (a per-user security stamp) or staff sign in with Entra ID |
| **HS256 with one key** | Validation by services that must not be able to issue tokens | A second service validates tokens; then RS256 with a published key set |
| **Tokenizing test card numbers at the API edge** ([assumption 5](assumptions.md)) | Keeping the API out of PCI scope entirely | Production: hosted payment fields move tokenization into the browser and out of our systems |
| **No script-src CSP on the dashboard yet** | Defense in depth against injected script | Sign-in lands (T26), with a nonce from `proxy.ts` |

## Operations and tooling

| Choice | What we gave up | We would revisit when |
| --- | --- | --- |
| **docker compose plus the standalone Aspire dashboard** instead of an Aspire AppHost | Service discovery and one-command startup from the .NET SDK | Every contributor already has the Aspire workload |
| **OpenTelemetry everywhere, exported only when configured** | Zero-config telemetry in a bare `dotnet run` | Not expected; the local dashboard is one compose service away |
| **SQL Server in Testcontainers for integration tests** | Speed: about 20 seconds per run instead of milliseconds | Never: an in-memory provider would hide exactly the locking and constraint behavior payments depend on |
| **Bicep validated, never deployed** | Proof that the Azure shape works end to end | Someone owns a subscription and the bill |
| **SQL Server over PostgreSQL** ([ADR 001](decisions/001-stack-and-repository-layout.md)) | The owner's usual default | Not for this exercise: the team runs Azure SQL |
| **Cache Components on in Next.js 16** | The simpler rendering model the house guides assume | Not expected: it becomes the only model in the next major |
