# Requirements traceability

The exercise restated in our own words, one row per requirement, with where it lives and its status.
Status is one of **Planned** (designed, not built), **Scaffolded** (the seam exists, the behavior does not), or **Done** (built and tested).
Task ids point at `notes/future.md`; where the brief was ambiguous, the choice is recorded in [assumptions.md](assumptions.md).

## Backend

| Id | Requirement | Where it lives | Task | Status |
| --- | --- | --- | --- | --- |
| R1 | A merchant user can log in | `POST /api/v1/auth/login`, [ADR 004](decisions/004-authentication-with-short-lived-jwts.md) | T19 | Planned |
| R2 | Create a payment | `POST /api/v1/payments` | T20 | Planned |
| R3 | Retrieve a payment's details | `GET /api/v1/payments/{id}` | T20 | Planned |
| R4 | Capture a payment | `POST /api/v1/payments/{id}/capture` | T21 | Planned |
| R5 | Refund a payment | `POST /api/v1/payments/{id}/refund` | T21 | Planned |
| R6 | List and search payments | `GET /api/v1/payments` with filters and a cursor | T22 | Planned |
| R7 | Payment endpoints require a logged-in merchant; the validation and expiry of a login are explainable | JWT bearer validation, [ADR 004](decisions/004-authentication-with-short-lived-jwts.md) | T19 | Planned |
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
| R16 | Log levels used deliberately | `payment-platform-error-handling-logging` overlay | T5 | Planned |
| R17 | No card numbers, CVV, or secrets in logs | Tokenization at the edge, no body logging, a test that greps the captured logs | T5, T20 | Planned |
| R18 | Request rate, error rate, and latency (RED) | ASP.NET Core and HttpClient OpenTelemetry meters | T5 | Planned |
| R19 | Payments created, captured, refunded, and failed, by state | `PaymentPlatform.Payments` meter | T25 | Planned |
| R20 | Queue depth, processing lag, retries, and failures for the async workflow | `PaymentPlatform.Outbox` meter | T25 | Planned |
| R21 | A trace spanning the API request and the async work | `traceparent` persisted with the outbox row and restored by the worker | T23 | Planned |
| R22 | A story for adopting distributed tracing in production | [ADR 005](decisions/005-observability-on-opentelemetry.md) | T2 | Planned |
| R23 | Health and readiness endpoints | `/health/live`, `/health/ready` on the API and the worker, integration-tested against live and dead databases | T5, T8, T9, T10 | Done |
| R24 | An audit trail of every payment state change: who, when, from, to | `PaymentEvents` table written in the same transaction as the change | T20, T21 | Planned |
| R25 | Consistent, structured error responses | RFC 9457 problem details with a stable `code`, [ADR 006](decisions/006-errors-as-problem-details.md); framework errors covered by integration tests | T8, T10 | Scaffolded |
| R26 | Idempotent create, capture, and refund | `Idempotency-Key` header, [ADR 007](decisions/007-idempotency-keys.md) | T20, T21 | Planned |
| R27 | Retries with backoff, timeouts, and dead-lettering in the async workflow | Outbox dispatcher and webhook delivery | T23 | Planned |

## Owner requirements

Added by the repository's owner on 2026-10-28, for demos and for reviewers who download the repository.

| Id | Requirement | Where it lives | Task | Status |
| --- | --- | --- | --- | --- |
| O1 | One command starts everything, with no setup | `docker compose up --build`; passwords and ports have defaults; the CI `stack` job runs it on a clean runner | T33 | Done |
| O2 | Every download it needs is listed | [README.md](../README.md#prerequisites) | T34 | Planned |
| O3 | Fully functional without any paid service | [README.md](../README.md#free-to-run) | T34 | Planned |
| O4 | Loads sample data and sample dashboards for a demo | A demo guide for what runs today; sample payments arrive with the payment features | T35 | Planned |

## Documentation deliverables

| Id | Deliverable | Where it lives | Task | Status |
| --- | --- | --- | --- | --- |
| D1 | Setup instructions for a local run | [README.md](../README.md), verified from a fresh clone | T18 | Done |
| D2 | Architectural overview | [architecture.md](architecture.md) | T2 | Done |
| D3 | Tradeoffs made | [tradeoffs.md](tradeoffs.md) and the ADRs; revisited in T30 | T18, T30 | Done |
| D4 | Assumptions | [assumptions.md](assumptions.md) | T2, T30 | Done |
| D5 | Production considerations | [production.md](production.md); revisited in T30 | T18, T30 | Done |
| D6 | Areas for future improvement | [future-improvements.md](future-improvements.md); revisited in T30 | T18, T30 | Done |
