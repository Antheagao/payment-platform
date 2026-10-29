# Future

Prioritized work ahead, with the reasoning.
Move items to `past.md` as they land and update `present.md`.
Task IDs match the task board in `present.md`.
This file is also the queue for the `batch-loop` skill (`.claude/skills/batch-loop/SKILL.md`): rows are taken top to bottom, and a `[USER-GATED: ...]` or `[BLOCKED: ...]` tag means skip it until the condition after the colon is met.

Last updated: 2026-10-28.

The scaffold (T1 to T18), merchant login (T19), and creating and reading payments (T20) are complete; their history is in `past.md`.
The owner asked on 2026-10-28 for the loop to run continuously through the requirements, each checked for correctness against the code, not only the docs.

## Now: the payment slice

Ranked by what the exercise grades most: the lifecycle and its audit trail, idempotency, the async workflow, then the dashboard.

### T21. Capture and refund

First slice, ready to start:
- `Application/Payments/CapturePayment.cs` and `RefundPayment.cs`: load the merchant's payment (another merchant's is `404`), move it through the domain (`payment_not_capturable` or `payment_not_refundable` as `409` when the state does not allow it), call the acquirer with the stored authorization reference outside any transaction, and save the change and its audit row; an acquirer decline is `502 acquirer_declined` with the payment unchanged, and an acquirer that does not answer is `503 acquirer_unavailable`, also unchanged.
- `POST /api/v1/payments/{id}/capture` and `/refund` with `Idempotency-Key` through the same `IdempotencyStore` (the fingerprint names the path and payment id), the second line of defense being the state check (ADR 007).
- The seeded authorized and captured payments can then be captured and refunded live; `docs/demo.md` step 5 gains the commands.
- Gate: the five-case checklist for both, double capture and double refund (`409` with a new key, replay with the same key), concurrent captures with one key, refund from captured and from settled, the acquirer's decline and silence leaving the payment as it was, and the audit rows for each.

### Then

- **T22. List and search.** Filters by status, date range, amount, and reference; keyset pagination with an opaque cursor; the indexes that serve it; enough seeded payments (a few hundred) that paging shows in the demo.
- **T23. Outbox and webhook delivery.** Outbox rows written in the same transaction as the state change, a dispatcher claiming rows with `READPAST`, HMAC-signed webhooks, exponential backoff with jitter, a dead-letter state, SSRF rules for merchant URLs, and trace context carried across the hop; the worker's first integration tests.
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
