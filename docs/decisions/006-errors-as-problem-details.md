# 006. Errors are RFC 9457 problem details with a stable code

- Status: accepted
- Date: 2026-10-28

## Context

Every error the API returns must have one predictable shape, including the ones the framework produces (unknown routes, wrong methods, malformed JSON, failed authentication).
Clients must be able to branch on an error without parsing English, and support must be able to find the logs for any error a merchant reports.
The claude-brain `api-design` guide prefers a `{"error": {"code", "message"}}` envelope until a service has third-party consumers; a payment API is consumed by merchants' own systems from day one.

## Decision

- Every non-2xx response is `application/problem+json` (RFC 9457) with `type`, `title`, `status`, and `detail`, plus these extension members:
  - `code`: a stable snake_case slug clients switch on (`payment_not_capturable`, `idempotency_key_reused`); never renamed or reused.
  - `correlation_id` and `trace_id`: the ids that find the request in the logs and traces.
  - `errors`: for validation failures, a map from snake_case field name to the codes that failed (`{"amount": ["amount_must_be_positive"]}`).
- `detail` is short, safe to show, and never contains SQL, stack traces, internal ids, or whether an account exists; the cause of a `500` is logged once and the response says only `internal_error`.
- The built-in `AddProblemDetails`, the exception handler, and status code pages produce the shape for framework errors, so a `404` for an unknown route, a `405`, and a `415` look like every other error.
- Use cases return typed results rather than throwing for expected failures (not found, invalid transition, conflict); only unexpected failures are exceptions.
- Status codes follow the global guide: `400` validation, `401` missing or expired token, `404` not found or another merchant's resource, `409` state conflict or idempotent request in flight, `422` idempotency key reused with a different body, `429` with `Retry-After`.

## Rejected alternatives

- **The claude-brain `{"error": {"code", "message"}}` envelope:** equally clear, but RFC 9457 is the shape .NET produces natively and that merchant tooling already understands, and the guide itself makes the exception once third parties consume the API.
- **Bare status codes with free-text bodies:** nothing to switch on.

## Consequences

- The frontend parses problem details in one place and switches on `code`.
- Adding a field later is additive; the media type never changes.

## Amendments

- 2026-10-28: "request id" renamed to "correlation id"; see the amendment in [ADR 005](005-observability-on-opentelemetry.md).
