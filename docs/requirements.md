# Requirements traceability

The exercise restated in our own words, one row per requirement, with where it lives and its status.
Status is one of **Planned** (designed, not built), **Scaffolded** (the seam exists, the behavior does not), or **Done** (built and tested).
Task ids point at `notes/future.md`; where the brief was ambiguous, the choice is recorded in [assumptions.md](assumptions.md).

## Backend

| Id | Requirement | Where it lives | Task | Status |
| --- | --- | --- | --- | --- |
| R1 | A merchant user can log in | `POST /api/v1/auth/login` (`Api/Endpoints/AuthEndpoints.cs`, `Application/Auth/LogIn.cs`): Identity's PBKDF2 hasher, one answer for an unknown email and a wrong password with the same hashing work, an upgraded hash at the next login, per-email and per-address limits answering `429` with `Retry-After`; `GET /api/v1/me` names the caller; the demo login is seeded in compose; `Auth/LoginTests` | T19 | Done |
| R2 | Create a payment | `POST /api/v1/payments` (`Application/Payments/CreatePayment.cs`): validation as field codes; the card tokenized at the edge ([ADR 008](decisions/008-card-tokenization-at-the-edge.md)); saved `Pending`, authorized outside any transaction, then `Authorized` or `Failed` with the decline code, or left `Pending` when the acquirer does not answer (assumption 7); `201` with `Location`; `CreatePaymentTests` | T20 | Done |
| R3 | Retrieve a payment's details | `GET /api/v1/payments/{id}` with its whole history, scoped to the caller's merchant (another merchant's payment is the same `404` as a missing one); `GetPaymentTests` | T20 | Done |
| R4 | Capture a payment | `POST /api/v1/payments/{id}/capture` | T21 | Planned |
| R5 | Refund a payment | `POST /api/v1/payments/{id}/refund` | T21 | Planned |
| R6 | List and search payments | `GET /api/v1/payments` with filters and a cursor | T22 | Planned |
| R7 | Payment endpoints require a logged-in merchant; the validation and expiry of a login are explainable | Every `/api/v1` endpoint requires the default policy unless it opts out, as only login does; HS256 pinned, issuer, audience, signature, and a 30-minute lifetime with 30 s of skew ([ADR 004](decisions/004-authentication-with-short-lived-jwts.md)); `401 token_expired` tells an expired token apart; the merchant id comes only from the token; `Auth/AccessTokenTests` (missing, expired, forged, tampered, unsigned, HS512, wrong issuer or audience, not yet valid, missing ids, another merchant) | T19 | Done |
| R8 | Lifecycle `Pending -> Authorized -> Captured -> Settled`, with `Failed` and `Refunded` as terminal branches | `PaymentLifecycle` and `Payment` in `PaymentPlatform.Domain`, every pair tested | T4 | Done |
| R9 | At least one asynchronous workflow | Webhook delivery over a transactional outbox, plus settlement, [ADR 003](decisions/003-async-workflow-on-a-transactional-outbox.md) | T23, T24 | Planned |

## Frontend

| Id | Requirement | Where it lives | Task | Status |
| --- | --- | --- | --- | --- |
| R10 | Log in to a merchant account | `/login` | T26 | Planned |
| R11 | View payments | `/payments` | T27 | Planned |
| R12 | Filter and search payments | `/payments` query string | T27 | Planned |
| R13 | View a payment's details and status history | `/payments/{id}` | T28 | Planned |

## Observability and operations

| Id | Requirement | Where it lives | Task | Status |
| --- | --- | --- | --- | --- |
| R14 | Structured (JSON) logs | JSON console formatter with scopes in `PaymentPlatform.ServiceDefaults` | T5 | Done |
| R15 | A correlation id carried from the API through the async workflow | `CorrelationIdMiddleware` (`X-Correlation-Id`); the id and W3C trace context stored on each outbox row | T5, T23 | Scaffolded |
| R16 | Log levels used deliberately | The policy in [ADR 005](decisions/005-observability-on-opentelemetry.md) (`Error` needs a person, `Warning` is unexpected but handled, `Information` is a business event, `Debug` is off); framework categories at `Warning` in `appsettings.json`; every message through `LoggerMessage` at a chosen level; `StartupTests` asserts a healthy start logs nothing at `Warning` or above. Done once the payment flows log their business events (T20, T21) | T5, T20 | Scaffolded |
| R17 | No card numbers, CVV, or secrets in logs | Tokenization at the edge and no body logging ([ADR 008](decisions/008-card-tokenization-at-the-edge.md)); tests that capture every log record at Trace and up: no password, email, or token in a login (`LoginTests`), and no card number, CVV, or token in the logs, the stored rows, or the responses of a create (`CreatePaymentTests`) | T5, T19, T20 | Done |
| R18 | Request rate, error rate, and latency (RED) | `http.server.request.duration` from the ASP.NET Core and HttpClient instrumentation: its count is the rate, `http.response.status_code` and `error.type` the errors, the histogram the latency; health probes are left out, as they are of the traces; `Telemetry/RequestMetricsTests` reads it back through the OpenTelemetry pipeline; charted in the Aspire dashboard | T5, T40 | Done |
| R19 | Payments created, captured, refunded, and failed, by state | `PaymentPlatform.Payments` meter | T25 | Planned |
| R20 | Queue depth, processing lag, retries, and failures for the async workflow | `PaymentPlatform.Outbox` meter | T25 | Planned |
| R21 | A trace spanning the API request and the async work | `traceparent` persisted with the outbox row and restored by the worker | T23 | Planned |
| R22 | A story for adopting distributed tracing in production | [ADR 005](decisions/005-observability-on-opentelemetry.md) (one vendor-neutral API, OTLP, the Aspire dashboard locally, the Azure Monitor distro in production), [architecture.md](architecture.md) trace continuity (the W3C `traceparent` stored on each outbox row and restored by the worker), [production.md](production.md) sampling | T2 | Done |
| R23 | Health and readiness endpoints | `/health/live`, `/health/ready` on the API and the worker, integration-tested against live and dead databases | T5, T8, T9, T10 | Done |
| R24 | An audit trail of every payment state change: who, when, from, to | `PaymentEvents` (actor kind and id, time, from, to, reason, correlation id), written in the same transaction as the change by `PaymentsDbContext.RecordChanges`; saving a payment with unrecorded changes throws, so no use case can skip it; returned as `history` on every payment. Every transition through the domain is covered; the settlement procedure (T24) writes its rows set-based and its gate checks them | T20 | Done |
| R25 | Consistent, structured error responses | RFC 9457 problem details with a stable `code`, `correlation_id`, and `trace_id` ([ADR 006](decisions/006-errors-as-problem-details.md)) for framework errors (404, 405, 415, unreadable bodies) and every endpoint's own (`400` with per-field codes, `401`, `403`, `404`, `422 card_not_accepted`, `429`), each asserted by `code` in the integration tests | T8, T10, T19, T20 | Done |
| R26 | Idempotent create, capture, and refund | `Idempotency-Key` required on `POST /api/v1/payments` ([ADR 007](decisions/007-idempotency-keys.md) and its amendment): the unique `(MerchantId, Key)` row is the lock, a retry replays the stored `201` with `Idempotent-Replayed: true`, a concurrent retry is `409`, a different request on the key is `422`, and a retry after a crash finishes the payment the dead request created; the fingerprint never covers card data; `IdempotencyTests` (six concurrent retries make one payment). Done for create; capture and refund reuse the store in T21 | T20, T21 | Scaffolded |
| R27 | Retries with backoff, timeouts, and dead-lettering in the async workflow | Outbox dispatcher and webhook delivery | T23 | Planned |

## Owner requirements

Added by the repository's owner on 2026-10-28, for demos and for reviewers who download the repository.

| Id | Requirement | Where it lives | Task | Status |
| --- | --- | --- | --- | --- |
| O1 | One command starts everything, with no setup | `docker compose up --build`; passwords and ports have defaults; the CI `stack` job runs it on a clean runner | T33 | Done |
| O2 | Every download it needs is listed | README, Prerequisites: to run, Git and Docker; to develop, the .NET SDK, Node.js 22, Playwright's Chromium, and optionally Bicep; each with its official page and a verified `winget` and Homebrew command | T34 | Done |
| O3 | Fully functional without any paid service | README, Free to run: every piece and its cost, with Docker Desktop's license limit and the free Docker Engine; every package license checked | T34 | Done |
| O4 | Loads sample data and sample dashboards for a demo | [demo.md](demo.md) walks the running stack and its Aspire dashboard today; a development seed loads a demo merchant (T19) and payments in every status with their audit trails (T20 to T22), applied on startup in compose and never in production | T35, T19 to T22 | Scaffolded |
| O5 | Screenshots and GIFs of the pages; the frontend checked in a browser and its design brought up to current practice | README, A look around, and [demo.md](demo.md): nine screenshots and an outage GIF in `docs/images/`, remade by `node scripts/capture-screenshots.mjs`; the home page's live service status and lifecycle flow; status colors balanced and gated by the contrast script; checked at 360 and 1440 in both schemes | T37 to T39 | Done |

## Documentation deliverables

| Id | Deliverable | Where it lives | Task | Status |
| --- | --- | --- | --- | --- |
| D1 | Setup instructions for a local run | [README.md](../README.md), verified from a fresh clone | T18 | Done |
| D2 | Architectural overview | [architecture.md](architecture.md) | T2 | Done |
| D3 | Tradeoffs made | [tradeoffs.md](tradeoffs.md) and the ADRs; revisited in T30 | T18, T30 | Done |
| D4 | Assumptions | [assumptions.md](assumptions.md) | T2, T30 | Done |
| D5 | Production considerations | [production.md](production.md); revisited in T30 | T18, T30 | Done |
| D6 | Areas for future improvement | [future-improvements.md](future-improvements.md); revisited in T30 | T18, T30 | Done |
