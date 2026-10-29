# Future

Prioritized work ahead, with the reasoning.
Move items to `past.md` as they land and update `present.md`.
Task IDs match the task board in `present.md`.
This file is also the queue for the `batch-loop` skill (`.claude/skills/batch-loop/SKILL.md`): rows are taken top to bottom, and a `[USER-GATED: ...]` or `[BLOCKED: ...]` tag means skip it until the condition after the colon is met.

Last updated: 2026-10-29.

The scaffold (T1 to T18), merchant login (T19), creating and reading payments (T20), capture and refund (T21), list and search (T22), the transactional outbox (T23a), the webhook endpoint API (T23b), signed webhook delivery (T23c), settlement (T24), the domain and queue metrics (T25), and the dashboard's sign-in (T26) are complete; their history is in `past.md`.
The owner asked on 2026-10-28 for the loop to run continuously through the requirements, each checked for correctness against the code, not only the docs.

## Now: the payment slice

Ranked by what the exercise grades most: the lifecycle and its audit trail, idempotency, the async workflow, then the dashboard.

### T27. Payments list

R11 and R12: `/payments`, the merchant's payments from `GET /api/v1/payments`, the first page that reads the merchant's data with the session.
- **The data layer.** `src/lib/payments-api.ts` (server-only): `listPayments(query)` sends the session token, turns the API's `401` into a redirect to `/login?next=...&reason=expired` (a `requireSession()` helper the later pages share) and its field codes into messages; `PaymentStatus` from `src/lib/payment-status.ts`, amounts formatted per currency (JPY has no minor unit).
- **The page.** A table from `lg` and a card list below it (status pill, amount, reference, card brand and last four, created time), newest first; filters bound to the URL (`status`, the date range, the amount range, `currency`, `reference` prefix) in a form that submits as `GET`, so every view has a link; `next_cursor` as "Older payments" with the cursor in the URL; an empty state for no payments and another for no matches; `error.tsx` for the API being down, `loading.tsx` with a same-size skeleton.
- **Header.** A "Payments" link for a signed-in merchant.
- Gate: Vitest for the query building, the money formatting, and the code mapping; Playwright against the stub (which gains a payments list) for the table, each filter, paging, the empty states, an ended session, and 360px; the browser walk-through on the composed stack with the 280 seeded payments; `docs/images/` remade with a payments screenshot; `docs/demo.md` walks the list.

### Then

- **T28. Payment detail.** Amounts, status, and the audit timeline.
- **T26b. Client address and the operator view.** The dashboard forwards the browser's address (`X-Forwarded-For`, trusted by the API only from the dashboard's network), so the per-address login limit applies per person; the service status panel moves behind an operator role, and merchants get a single "payments are delayed" banner (docs/tradeoffs.md).
- Every feature row also updates `docs/demo.md` with the step that shows it, verified against the composed stack, and a row that changes what a page shows remakes `docs/images/` with `node scripts/capture-screenshots.mjs`.
- **T29. End-to-end golden path.** Playwright: log in, find a payment, open its timeline; CI job against the composed stack.
- **T30. Documentation complete.** Tradeoffs, assumptions, production considerations, and future improvements rewritten against what was actually built.
- **T31. Load test.** A k6 script in a container against the composed stack, with the numbers and the bottleneck written down.

## Smaller items to fold in

- A replay for dead-lettered webhook deliveries (ADR 003): `POST` on a delivery that puts it back to `Pending` with its attempts reset, for an operator role; fold into T26b, which brings roles to the dashboard (the `webhook.deliveries.dead_lettered` gauge already counts what it would replay).
- Integration tests: migrate one template database per run and copy it for each test class (a backup and restore, or `CREATE DATABASE ... AS SNAPSHOT` where the edition allows), instead of migrating 30-odd databases; the per-class setup is now about 3.5 s and the suite about 60 s, and it grows with every migration.
- OpenTelemetry in the dashboard (`instrumentation.ts`) so traces start at the dashboard's server; fold into T27, whose data reads would show in the trace.
- Update this machine's .NET SDK past 10.0.101: CI runs 10.0.403, whose analyzers caught an unused using the local SDK missed. [USER-GATED: installing an SDK changes the machine; the owner's call.]
- Five Dependabot pull requests (#1 to #5) from the first scan: #2 groups minor and patch bumps and can merge once CI is green; #1, #3, #4, and #5 are majors the new ignore rules now hold back. [USER-GATED: merging or closing them is the owner's call.]

## Later: parked

- **Python reconciliation export** for a Power BI dataset. [BLOCKED: only if the settlement work (T24) shows a pipeline is needed; the brief says Python only if needed.]
- **Azure DevOps pipeline** mirroring the GitHub Actions gates. [USER-GATED: an Azure DevOps organization to run it in; an unrun pipeline would be unverified.]
- **Real Azure deployment.** [USER-GATED: never for this repository; portfolio rule is validate only.]
