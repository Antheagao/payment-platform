# Future

Prioritized work ahead, with the reasoning.
Move items to `past.md` as they land and update `present.md`.
Task IDs match the task board in `present.md`.
This file is also the queue for the `batch-loop` skill (`.claude/skills/batch-loop/SKILL.md`): rows are taken top to bottom, and a `[USER-GATED: ...]` or `[BLOCKED: ...]` tag means skip it until the condition after the colon is met.

Last updated: 2026-10-29.

The scaffold (T1 to T18), merchant login (T19), creating and reading payments (T20), capture and refund (T21), and list and search (T22) are complete; their history is in `past.md`.
The owner asked on 2026-10-28 for the loop to run continuously through the requirements, each checked for correctness against the code, not only the docs.

## Now: the payment slice

Ranked by what the exercise grades most: the lifecycle and its audit trail, idempotency, the async workflow, then the dashboard.

### T23. Outbox and webhook delivery

The asynchronous workflow (R9, R15, R20, R21, R27; ADR 003). Two slices:
- **T23a. The outbox.** An `OutboxMessages` table written in the same transaction as every status change (`RecordChanges` adds one per `PaymentStatusChanged`: event type `payment.<status>`, the payment's public JSON, the correlation id, and the W3C `traceparent` of the request), and a worker job that claims due rows with `UPDATE TOP (@n) ... WITH (UPDLOCK, READPAST, ROWLOCK) OUTPUT inserted.*`, restores the correlation id and trace context for each, and hands it to the delivery step; the seed's history writes no outbox rows (its events are history, not news).
- **T23b. Signed webhook delivery.** A merchant's webhook endpoint and signing secret (a table, managed by a small API), deliveries signed with HMAC-SHA256 over the timestamp and raw body, retries with exponential backoff and full jitter from 10 seconds to 1 hour, a dead-letter state after the last attempt, SSRF rules for merchant URLs (security overlay), and a local receiver in compose so the demo shows deliveries arriving.
- Gate: a status change and its outbox row commit or roll back together; two workers never claim one row; a trace runs from `POST /payments` through the worker's delivery; retries back off and dead-letter; signatures verify; a private or loopback URL is refused.

### Then

- **T24. Settlement.** A scheduled batch that settles captured payments through a T-SQL stored procedure, with reconciliation counts.
- **T25. Domain and queue metrics.** Payments created, captured, refunded, failed by state; outbox depth, oldest-pending lag, retries, dead letters.
- **T26. Dashboard login.** Login form, the JWT held in an HttpOnly cookie on the Next.js server, sign-out, expiry handling, and a nonce-based CSP from `proxy.ts`; the home page's service status moves behind an operator role, and merchants get a single banner when payments are delayed (docs/tradeoffs.md).
- **T27. Payments list.** Filters and search bound to the URL, pagination, empty and error states.
- **T28. Payment detail.** Amounts, status, and the audit timeline.
- Every feature row also updates `docs/demo.md` with the step that shows it, verified against the composed stack, and a row that changes what a page shows remakes `docs/images/` with `node scripts/capture-screenshots.mjs`.
- **T29. End-to-end golden path.** Playwright: log in, find a payment, open its timeline; CI job against the composed stack.
- **T30. Documentation complete.** Tradeoffs, assumptions, production considerations, and future improvements rewritten against what was actually built.
- **T31. Load test.** A k6 script in a container against the composed stack, with the numbers and the bottleneck written down.

## Smaller items to fold in

- OpenTelemetry in the dashboard (`instrumentation.ts`) so traces start at the browser request; fold into T26.
- Update this machine's .NET SDK past 10.0.101: CI runs 10.0.403, whose analyzers caught an unused using the local SDK missed. [USER-GATED: installing an SDK changes the machine; the owner's call.]
- Five Dependabot pull requests (#1 to #5) from the first scan: #2 groups minor and patch bumps and can merge once CI is green; #1, #3, #4, and #5 are majors the new ignore rules now hold back. [USER-GATED: merging or closing them is the owner's call.]

## Later: parked

- **Python reconciliation export** for a Power BI dataset. [BLOCKED: only if the settlement work (T24) shows a pipeline is needed; the brief says Python only if needed.]
- **Azure DevOps pipeline** mirroring the GitHub Actions gates. [USER-GATED: an Azure DevOps organization to run it in; an unrun pipeline would be unverified.]
- **Real Azure deployment.** [USER-GATED: never for this repository; portfolio rule is validate only.]
