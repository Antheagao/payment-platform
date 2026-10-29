# Future

Prioritized work ahead, with the reasoning.
Move items to `past.md` as they land and update `present.md`.
Task IDs match the task board in `present.md`.
This file is also the queue for the `batch-loop` skill (`.claude/skills/batch-loop/SKILL.md`): rows are taken top to bottom, and a `[USER-GATED: ...]` or `[BLOCKED: ...]` tag means skip it until the condition after the colon is met.

Last updated: 2026-10-29.

The scaffold (T1 to T18), merchant login (T19), creating and reading payments (T20), capture and refund (T21), list and search (T22), the transactional outbox (T23a), the webhook endpoint API (T23b), signed webhook delivery (T23c), settlement (T24), the domain and queue metrics (T25), the dashboard's sign-in (T26), the payments list (T27), and the payment detail (T28) are complete; their history is in `past.md`.
The owner asked on 2026-10-28 for the loop to run continuously through the requirements, each checked for correctness against the code, not only the docs.

## Now: the payment slice

Ranked by what the exercise grades most: the lifecycle and its audit trail, idempotency, the async workflow, then the dashboard.

### T26b. Client address and the operator view

The dashboard is the API's only browser-facing caller, so today every sign-in reaches the API from the dashboard's address and the per-address login limit is one limit for everyone (docs/tradeoffs.md); and the service status panel shows merchants the platform's internals.
- **Client address.** The dashboard's server sends the browser's address as `X-Forwarded-For` on sign-in; the API's forwarded-headers middleware trusts it only from the dashboard's known network (compose's network locally, the dashboard's subnet in the Bicep), and the per-address limit keys on it. First check what address Next.js 16 gives a Server Action behind compose (the socket's, or a header it sets), from the running stack, not the docs alone.
- **Operator view.** A role on the merchant user (`merchant` or `operator`) carried in the token; the service status panel on the home page for operators only; a merchant sees one "payments are delayed" banner when the API reports not ready, and nothing about the database or the worker.
- Fold in the dead-letter replay below, which needs the operator role.
- Gate: integration tests for a trusted and an untrusted `X-Forwarded-For` (a spoofed header from outside the known network keys on the real address), the per-address limit for two forwarded addresses, the role in the token and the replay's authorization; Vitest for the banner; Playwright against the stub for both roles; the browser walk-through on the composed stack signed in as each role; `docs/demo.md` and the images updated where the home page changes.

### Then

- Every feature row also updates `docs/demo.md` with the step that shows it, verified against the composed stack, and a row that changes what a page shows remakes `docs/images/` with `node scripts/capture-screenshots.mjs`.
- **T29. End-to-end golden path.** Playwright: log in, find a payment, open its timeline; CI job against the composed stack.
- **T30. Documentation complete.** Tradeoffs, assumptions, production considerations, and future improvements rewritten against what was actually built.
- **T31. Load test.** A k6 script in a container against the composed stack, with the numbers and the bottleneck written down.

## Smaller items to fold in

- A replay for dead-lettered webhook deliveries (ADR 003): `POST` on a delivery that puts it back to `Pending` with its attempts reset, for an operator role; fold into T26b, which brings roles to the API and the dashboard (the `webhook.deliveries.dead_lettered` gauge already counts what it would replay).
- Integration tests: migrate one template database per run and copy it for each test class (a backup and restore, or `CREATE DATABASE ... AS SNAPSHOT` where the edition allows), instead of migrating 30-odd databases; the per-class setup is now about 3.5 s and the suite about 60 s, and it grows with every migration.
- OpenTelemetry in the dashboard (`instrumentation.ts`) so traces start at the dashboard's server, and a capture from a payment's page is one trace from the click to the webhook; fold into T29, which runs on the composed stack where that trace can be checked in Aspire (T28 left it out to keep its scope to the page).
- Update this machine's .NET SDK past 10.0.101: CI runs 10.0.403, whose analyzers caught an unused using the local SDK missed. [USER-GATED: installing an SDK changes the machine; the owner's call.]
- Five Dependabot pull requests (#1 to #5) from the first scan: #2 groups minor and patch bumps and can merge once CI is green; #1, #3, #4, and #5 are majors the new ignore rules now hold back. [USER-GATED: merging or closing them is the owner's call.]

## Later: parked

- **Python reconciliation export** for a Power BI dataset. [BLOCKED: only if the settlement work (T24) shows a pipeline is needed; the brief says Python only if needed.]
- **Azure DevOps pipeline** mirroring the GitHub Actions gates. [USER-GATED: an Azure DevOps organization to run it in; an unrun pipeline would be unverified.]
- **Real Azure deployment.** [USER-GATED: never for this repository; portfolio rule is validate only.]
