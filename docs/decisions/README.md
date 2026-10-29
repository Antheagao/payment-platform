# Decision records

Choices that are hard to reverse, cross layers, or reject a reasonable alternative.
Each record states its context, the decision, the alternatives rejected, and the consequences.
An accepted record is never rewritten; a new record supersedes it, and the old one gains a link to the new one.

| Number | Decision | Status |
| --- | --- | --- |
| [001](001-stack-and-repository-layout.md) | Stack and repository layout | Accepted |
| [002](002-backend-project-boundaries.md) | Backend project boundaries | Accepted |
| [003](003-async-workflow-on-a-transactional-outbox.md) | The asynchronous workflow runs on a transactional outbox | Accepted |
| [004](004-authentication-with-short-lived-jwts.md) | Authentication with short-lived JWTs, held by the dashboard's server | Accepted |
| [005](005-observability-on-opentelemetry.md) | Observability on OpenTelemetry | Accepted |
| [006](006-errors-as-problem-details.md) | Errors are RFC 9457 problem details with a stable code | Accepted |
| [007](007-idempotency-keys.md) | Idempotency keys for create, capture, and refund | Accepted |
| [008](008-card-tokenization-at-the-edge.md) | Card tokenization at the edge, with a vault that holds only test cards | Accepted |
| [009](009-strict-csp-with-per-request-rendering.md) | A nonce-based Content-Security-Policy, with every dashboard page rendered per request | Accepted |
