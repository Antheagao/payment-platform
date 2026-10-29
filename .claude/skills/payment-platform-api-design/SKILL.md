---
name: payment-platform-api-design
description: Payment Platform HTTP API facts and local exceptions - /api/v1 routing, problem details, snake_case JSON, money in minor units, idempotency, pagination, limits, and the endpoint checklist for ASP.NET Core minimal APIs. Use when adding or changing an endpoint or a JSON contract in this repository.
---

# Payment Platform api-design overlay

Load the global `api-design` guide for the rules; this file says what exists, translates the Rust specifics to ASP.NET Core, and records the local exceptions.
ADR 006 (errors) and ADR 007 (idempotency) come first.

## What exists (`backend/src/PaymentPlatform.Api`)

- `Endpoints/ApiRoutes.cs`: `MapApiV1()` creates the `/api/v1` group and requires the default authorization policy on all of it; an endpoint opts out only with `AllowAnonymous`, as login does.
- `Endpoints/AuthEndpoints.cs`: `POST /api/v1/auth/login` (`{"email", "password"}` to `{"access_token", "token_type": "Bearer", "expires_at", "expires_in"}`; `400` with `errors` codes such as `email_invalid`, `401 invalid_credentials` with `WWW-Authenticate: Bearer`, `429 rate_limited` with `Retry-After`) and `GET /api/v1/me` (`user_id`, `merchant_id`, `email`, `merchant_name`).
- A missing or rejected token is `401` with `code` `unauthorized`, or `token_expired` for an expired one, and an RFC 6750 `WWW-Authenticate` header; a body that does not bind is `400` in every environment (`ThrowOnBadRequest` is off), and 400, 415, and 429 problems carry a plain `detail`.
  Health stays outside it at `/health/live` and `/health/ready`.
- JSON: `JsonNamingPolicy.SnakeCaseLower` for properties and enums (`"status": "captured"`); unknown request fields are ignored (System.Text.Json's default).
- Errors: `Errors/ProblemDetailsSetup.cs` gives every non-2xx response, including unknown routes (404) and wrong methods (405), the RFC 9457 shape with `code`, `correlation_id`, and `trace_id`; `Errors/ProblemCodes.cs` holds the default code per status.
- Limits: request bodies 64 KB (Kestrel), requests 10 s (`503`, code `service_unavailable`).
- OpenAPI: the built-in generator at `/openapi/v1.json`, Development only.

## Local exceptions to the global guide

- Errors are RFC 9457 problem details, not `{"error": {"code", "message"}}` (ADR 006); `code` keeps the same role, `detail` plays `message`.
- Money is `amount_minor` (an integer in the currency's minor unit) plus `currency`, not `*_cents`, because the minor unit is a cent only in some currencies (JPY has none).
- Ids are GUID strings; timestamps are UTC `DateTimeOffset`, serialized ISO 8601 with a `+00:00` offset, everywhere.

## Translations from the Rust guide

- One request record and one response record per endpoint, in the use case file or beside the endpoint; never bind to a domain type or return an entity.
- Validation happens before any query and reports codes, not messages: `errors: {"amount_minor": ["amount_must_be_positive"]}` with field names in snake_case.
- Endpoints return `Results<Ok<T>, ProblemHttpResult, ...>` (typed results), so OpenAPI shows every status and the compiler checks the branches.
- The merchant id always comes from the validated token's claims, never from the path, query, or body.

## Payments-specific rules

- `POST /payments`, `/payments/{id}/capture`, and `/payments/{id}/refund` require `Idempotency-Key` (ADR 007); a replay carries `Idempotent-Replayed: true`.
- Lists: `?limit=` (default 25, maximum 100; out of range is `400 invalid_limit`), an opaque base64url `cursor` over `(created_at, id)`, `next_cursor` null on the last page, results wrapped as `{"data": [...], "next_cursor": ...}`.
- Another merchant's payment is `404 not_found`, indistinguishable from a missing one.

## Checklist for a new endpoint

- One `Map<Resource>Endpoints(this RouteGroupBuilder group)` in `Endpoints/<Resource>Endpoints.cs`, called from `Program.cs`.
- `.WithName()`, typed results, and `.ProducesProblem()` for every error status, so `/openapi/v1.json` is complete.
- Integration tests for the five cases in the global `testing` guide, asserting `code`, never `detail` text.
- The dashboard's types and calls change in the same commit as the contract.
