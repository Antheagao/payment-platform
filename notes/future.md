# Future

Prioritized work ahead, with the reasoning.
Move items to `past.md` as they land and update `present.md`.
Task IDs match the task board in `present.md`.
This file is also the queue for the `batch-loop` skill (`.claude/skills/batch-loop/SKILL.md`): rows are taken top to bottom, and a `[USER-GATED: ...]` or `[BLOCKED: ...]` tag means skip it until the condition after the colon is met.

Last updated: 2026-10-29.

The scaffold (T1 to T18), merchant login (T19), creating and reading payments (T20), capture and refund (T21), list and search (T22), the transactional outbox (T23a), and the webhook endpoint API (T23b) are complete; their history is in `past.md`.
The owner asked on 2026-10-28 for the loop to run continuously through the requirements, each checked for correctness against the code, not only the docs.

## Now: the payment slice

Ranked by what the exercise grades most: the lifecycle and its audit trail, idempotency, the async workflow, then the dashboard.

### T23c. Signed webhook delivery

The rest of the asynchronous workflow (R9, R15, R27; ADR 003 and its 2026-10-29 amendment). The outbox and dispatcher (T23a) and the endpoint API with the set-time SSRF rules (T23b) have landed.
- **The delivery job.** Claims due `Pending` deliveries with `UPDATE TOP (@n) ... WITH (UPDLOCK, READPAST, ROWLOCK) ... OUTPUT inserted.*` and a lease (`LeasedUntil`), posts the payload with `Webhook-Id`, `Webhook-Timestamp`, and `Webhook-Signature` (HMAC-SHA256 over `id.timestamp.body`, `v1,` and base64) and the correlation id, a 10 s timeout, no redirects; `2xx` delivered, `410` disables the endpoint and dead-letters, anything else retries with exponential backoff and full jitter from 10 s to 1 h, dead-lettered after 12 attempts; a disabled endpoint's pending deliveries are dead-lettered; a `webhook deliver` `Producer` span under the event's trace, and the job opts out of per-run traces like the dispatcher.
- **Connect-time SSRF guard.** A named `HttpClient` whose `SocketsHttpHandler.ConnectCallback` resolves through `WebhookUrlPolicy.ResolveAllowedAsync` and connects only to an allowed address, so a name that resolves somewhere private by the time of the send reaches nothing; no proxy, no redirects, response bodies not read past a few KB.
- **A local receiver** in compose (a small container that verifies signatures and logs what it gets, listed in `Webhooks:LocalReceiverHosts` for Development), so the demo shows deliveries arriving and a forced failure retrying.
- Gate: signatures verify byte for byte (a test vector in the docs); a rebinding name is refused at connect time; retries back off on a fake clock and dead-letter; two delivery workers never send one delivery at once; an expired lease is taken over; the trace runs from `POST /payments` through dispatch into delivery in the Aspire dashboard.

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

- Integration tests: migrate one template database per run and copy it for each test class (a backup and restore, or `CREATE DATABASE ... AS SNAPSHOT` where the edition allows), instead of migrating 30-odd databases; the per-class setup is now about 3.5 s and the suite about 60 s, and it grows with every migration.
- OpenTelemetry in the dashboard (`instrumentation.ts`) so traces start at the browser request; fold into T26.
- Update this machine's .NET SDK past 10.0.101: CI runs 10.0.403, whose analyzers caught an unused using the local SDK missed. [USER-GATED: installing an SDK changes the machine; the owner's call.]
- Five Dependabot pull requests (#1 to #5) from the first scan: #2 groups minor and patch bumps and can merge once CI is green; #1, #3, #4, and #5 are majors the new ignore rules now hold back. [USER-GATED: merging or closing them is the owner's call.]

## Later: parked

- **Python reconciliation export** for a Power BI dataset. [BLOCKED: only if the settlement work (T24) shows a pipeline is needed; the brief says Python only if needed.]
- **Azure DevOps pipeline** mirroring the GitHub Actions gates. [USER-GATED: an Azure DevOps organization to run it in; an unrun pipeline would be unverified.]
- **Real Azure deployment.** [USER-GATED: never for this repository; portfolio rule is validate only.]
