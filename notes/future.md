# Future

Prioritized work ahead, with the reasoning.
Move items to `past.md` as they land and update `present.md`.
Task IDs match the task board in `present.md`.
This file is also the queue for the `batch-loop` skill (`.claude/skills/batch-loop/SKILL.md`): rows are taken top to bottom, and a `[USER-GATED: ...]` or `[BLOCKED: ...]` tag means skip it until the condition after the colon is met.

Last updated: 2026-10-29.

The scaffold (T1 to T18), merchant login (T19), creating and reading payments (T20), capture and refund (T21), list and search (T22), the transactional outbox (T23a), the webhook endpoint API (T23b), signed webhook delivery (T23c), settlement (T24), the domain and queue metrics (T25), and the dashboard's sign-in (T26), and the payments list (T27) are complete; their history is in `past.md`.
The owner asked on 2026-10-28 for the loop to run continuously through the requirements, each checked for correctness against the code, not only the docs.

## Now: the payment slice

Ranked by what the exercise grades most: the lifecycle and its audit trail, idempotency, the async workflow, then the dashboard.

### T28. Payment detail

R13: `/payments/{id}`, a payment's amounts, card, status, and its audit timeline from `GET /api/v1/payments/{id}`, linked from every row of the list.
- **The page.** The amount and status at the top; the card (brand, last four, expiry), the merchant reference, the authorization reference, the failure reason when it failed; the history as an ordered timeline, oldest first: each change's from and to statuses, who made it (the merchant user or the `system` job by name), when (UTC), and its correlation id, so a support question can be traced in the logs.
- **Actions.** Capture an authorized payment and refund a captured or settled one from the page, through Server Actions that send an `Idempotency-Key` made for each submission (a resubmission of the same form reuses it), show the API's codes (`payment_not_capturable`, `capture_declined`, `acquirer_unavailable` with its retry) as messages, and confirm before a refund.
- **States.** `not-found.tsx` for an id that is not the merchant's (the API's `404`), `error.tsx` for the API down, an ended session sent to sign in and back.
- Gate: Vitest for the timeline's wording and the action's code mapping; Playwright against the stub (which gains a payment, its history, and the two moves) for the page, capture, refund with its confirmation, the refused move, the 404, and 360px; the browser walk-through on the composed stack with `INV-1004` (the whole lifecycle) and `INV-1025`; `docs/images/` remade with a detail screenshot; `docs/demo.md` opens a payment.

### Then

- **T26b. Client address and the operator view.** The dashboard forwards the browser's address (`X-Forwarded-For`, trusted by the API only from the dashboard's network), so the per-address login limit applies per person; the service status panel moves behind an operator role, and merchants get a single "payments are delayed" banner (docs/tradeoffs.md).
- Every feature row also updates `docs/demo.md` with the step that shows it, verified against the composed stack, and a row that changes what a page shows remakes `docs/images/` with `node scripts/capture-screenshots.mjs`.
- **T29. End-to-end golden path.** Playwright: log in, find a payment, open its timeline; CI job against the composed stack.
- **T30. Documentation complete.** Tradeoffs, assumptions, production considerations, and future improvements rewritten against what was actually built.
- **T31. Load test.** A k6 script in a container against the composed stack, with the numbers and the bottleneck written down.

## Smaller items to fold in

- A replay for dead-lettered webhook deliveries (ADR 003): `POST` on a delivery that puts it back to `Pending` with its attempts reset, for an operator role; fold into T26b, which brings roles to the dashboard (the `webhook.deliveries.dead_lettered` gauge already counts what it would replay).
- Integration tests: migrate one template database per run and copy it for each test class (a backup and restore, or `CREATE DATABASE ... AS SNAPSHOT` where the edition allows), instead of migrating 30-odd databases; the per-class setup is now about 3.5 s and the suite about 60 s, and it grows with every migration.
- OpenTelemetry in the dashboard (`instrumentation.ts`) so traces start at the dashboard's server; fold into T28, whose data reads and moves would show in the trace.
- Update this machine's .NET SDK past 10.0.101: CI runs 10.0.403, whose analyzers caught an unused using the local SDK missed. [USER-GATED: installing an SDK changes the machine; the owner's call.]
- Five Dependabot pull requests (#1 to #5) from the first scan: #2 groups minor and patch bumps and can merge once CI is green; #1, #3, #4, and #5 are majors the new ignore rules now hold back. [USER-GATED: merging or closing them is the owner's call.]

## Later: parked

- **Python reconciliation export** for a Power BI dataset. [BLOCKED: only if the settlement work (T24) shows a pipeline is needed; the brief says Python only if needed.]
- **Azure DevOps pipeline** mirroring the GitHub Actions gates. [USER-GATED: an Azure DevOps organization to run it in; an unrun pipeline would be unverified.]
- **Real Azure deployment.** [USER-GATED: never for this repository; portfolio rule is validate only.]
