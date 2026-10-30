# Tradeoffs

What we chose, what we gave up, and when we would choose differently.
The decision records in [decisions/](decisions/README.md) carry the full reasoning for the larger choices; this page is the short version, plus the smaller choices that did not need a record.

## Architecture

| Choice | What we gave up | We would revisit when |
| --- | --- | --- |
| **A transactional outbox polled from SQL Server** for the async workflow ([ADR 003](decisions/003-async-workflow-on-a-transactional-outbox.md)) | Up to a second of latency, and the database carries the queue load | Event volume outgrows one database; the outbox then relays to Azure Service Bus, which the Bicep already provisions |
| **Six backend projects** with dependencies pointing inward ([ADR 002](decisions/002-backend-project-boundaries.md)) | More projects than the smallest layout | Never fewer than the boundaries need; an architecture test fails if one is crossed |
| **No repository layer over EF Core** | A seam for unit-testing use cases without a database | Never for this team size: integration tests run on real SQL Server in about a minute |
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
| **The dashboard names each person's address under a shared key** ([ADR 010](decisions/010-client-address-from-the-dashboard.md)) | One more secret to rotate, and one more container in compose (the Caddy edge) | The API gets a gateway of its own that authenticates the dashboard (mutual TLS or a managed identity token), which would replace the key |
| **Platform staff as their own identity, with a local password** ([ADR 011](decisions/011-platform-staff-as-their-own-identity.md)) | One sign-in path more to keep secure, and two accounts for a person who is both a merchant's user and an operator | Staff sign in with Entra ID in Azure; the local password stays only for compose |
| **HS256 with one key** | Validation by services that must not be able to issue tokens | A second service validates tokens; then RS256 with a published key set |
| **Tokenizing test card numbers at the API edge** ([assumption 5](assumptions.md)) | Keeping the API out of PCI scope entirely | Production: hosted payment fields move tokenization into the browser and out of our systems |
| **Public, local-only database passwords as compose defaults** | The rule that no password ever appears in a repository | Never for Azure, which has no passwords: the services use Entra managed identities; locally every port binds to 127.0.0.1 |
| **Every dashboard page rendered per request, for a nonce-based Content-Security-Policy** ([ADR 009](decisions/009-strict-csp-with-per-request-rendering.md)) | Prerendered shells: a server render per page view, and HTML a CDN cannot cache | Next.js gives prerendered pages a policy that does not need `'unsafe-inline'` (its experimental Subresource Integrity covers the script files, not the inline data) |
| **A payment that is not found answers `200`, marked `noindex`** | The `404` status: every page streams (ADR 009), so the status is sent before the payment is read; the redirect to sign in an ended session also happens in the browser rather than as a `307` | A client of the dashboard reads its statuses; nothing does today, and the API itself answers `404` |

## Operations and tooling

| Choice | What we gave up | We would revisit when |
| --- | --- | --- |
| **docker compose plus the standalone Aspire dashboard** instead of an Aspire AppHost | Service discovery and generated local passwords from the .NET SDK | Every reader already has the .NET SDK; today a reviewer needs only Docker |
| **An idempotency key stores only a request that created a payment** (ADR 007 amendment) | Replaying a validation error: a corrected request may reuse the key | Not expected; it is Stripe's rule, and nothing was created that a replay would protect |
| **The idempotency fingerprint leaves out the card number** | Telling apart two requests that differ only in the middle digits of the card | Never: hashing the number would put card data in the table |
| **Screenshots and the GIF are committed, captured from the running stack by a script** | About 2.2 MB of images in the repository, and images that go stale until someone reruns the script; CI does not remake them | Pages change often enough that stale images slip through review; then a CI job captures them against the composed stack and fails on a visual diff |
| **The dashboard's home shows each service's readiness, refreshed every 5 s** | A merchant view free of infrastructure words, and a steady trickle of readiness probes while a tab is open (one API and one worker probe per tab every 5 s, none while hidden or paused) | The dashboard's operator area exists (T26d, on the staff identity of ADR 011): the panel moves there, and merchants get a single "payments are delayed" banner |
| **Readiness probes run on the thread pool and are abandoned at a 3 s deadline** | A thread-pool thread held for the length of a host-name lookup during an outage | SqlClient's `OpenAsync` stops resolving host names synchronously |
| **OpenTelemetry everywhere, exported only when configured** | Zero-config telemetry in a bare `dotnet run` | Not expected; the local dashboard is one compose service away |
| **SQL Server in Testcontainers for integration tests** | Speed: about a minute per run instead of seconds (each test class migrates a database of its own, about 3.5 s) | Never: an in-memory provider would hide exactly the locking and constraint behavior payments depend on |
| **Bicep validated, never deployed** | Proof that the Azure shape works end to end | Someone owns a subscription and the bill |
| **SQL Server over PostgreSQL** ([ADR 001](decisions/001-stack-and-repository-layout.md)) | The owner's usual default | Not for this exercise: the team runs Azure SQL |
| **Cache Components on in Next.js 16** | The simpler rendering model the house guides assume | Not expected: it becomes the only model in the next major |
