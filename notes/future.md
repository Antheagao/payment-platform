# Future

Prioritized work ahead, with the reasoning.
Move items to `past.md` as they land and update `present.md`.
Task IDs match the task board in `present.md`.
This file is also the queue for the `batch-loop` skill (`.claude/skills/batch-loop/SKILL.md`): rows are taken top to bottom, and a `[USER-GATED: ...]` or `[BLOCKED: ...]` tag means skip it until the condition after the colon is met.

Last updated: 2026-10-29.

The scaffold (T1 to T18), merchant login (T19), creating and reading payments (T20), capture and refund (T21), list and search (T22), the transactional outbox (T23a), the webhook endpoint API (T23b), signed webhook delivery (T23c), settlement (T24), the domain and queue metrics (T25), the dashboard's sign-in (T26), the payments list (T27), the payment detail (T28), the client's address per person (T26b), the golden path through the composed stack (T29), traces from the dashboard's server (T29b), the load test (T31), and the documentation checked against the code (T30) are complete; their history is in `past.md`.
The owner asked on 2026-10-28 for the loop to run continuously through the requirements, each checked for correctness against the code, not only the docs.

## Now: the payment slice

Ranked by what the exercise grades most: the lifecycle and its audit trail, idempotency, the async workflow, then the dashboard.

### T41. Reconcile payments left Pending

Found in T30: `docs/architecture.md` said the worker reconciles payments stuck in `Pending`, but no such job exists (the worker's own comment calls it next); the docs now say planned.
A payment stays `Pending` when the acquirer does not answer during create (test card `4000 0000 0000 0119`), or when the process dies between the acquirer's answer and the save.
- **The job.** `ReconcilePendingPayments` in the worker, every minute: claims `Pending` payments older than a grace period (`UPDLOCK, READPAST`, like the outbox), asks the acquirer to authorize again with the payment id as its idempotency key (`IAcquirer` already promises that), and moves each to `Authorized` or `Failed` with an audit row under the `system` actor `reconciliation` and the outbox event; an acquirer that still does not answer leaves it for the next run, and after a deadline (a day) the payment fails with `acquirer_unreachable`.
- **The simulator** must remember an answered authorization per payment id, or a retried one could decide differently; check `SimulatedAcquirer` (stateless today) before writing the job, and decide whether the no-answer test card answers on the second try.
- Gate: integration tests on the fake clock (resolved to authorized, to failed, still unanswered, past the deadline, two workers sharing the rows, the audit row and event), the metric for payments still pending, `docs/demo.md` showing the `0119` payment resolve, and the architecture, assumptions, and production docs updated back from planned.

### T26c. The operator view

The service status panel shows every merchant the platform's internals (the database, the worker), which only an operator needs.
- **Roles.** A role on the merchant user (`merchant` or `operator`), seeded for a second demo login, carried in the token as a claim and checked by the API's policies, never by the dashboard alone.
- **The dashboard.** The home page's service status panel for operators only; a merchant sees one "payments are delayed" banner when the API reports not ready, and nothing about the database or the worker. The header's API badge follows the same rule.
- Fold in the dead-letter replay below, which needs the operator role.
- Gate: integration tests for the role in the token, the policy on the replay (a merchant's token is `403`), the replay itself (back to `Pending`, attempts reset, delivered on the next run); Vitest for the banner; Playwright against the stub for both roles; the browser walk-through on the composed stack signed in as each; `docs/demo.md` and the images where the home page changes.

### Then

- Every feature row also updates `docs/demo.md` with the step that shows it, verified against the composed stack, and a row that changes what a page shows remakes `docs/images/` with `node scripts/capture-screenshots.mjs`.

## Smaller items to fold in

- A replay for dead-lettered webhook deliveries (ADR 003): `POST` on a delivery that puts it back to `Pending` with its attempts reset, for an operator role; fold into T26c, which brings roles to the API and the dashboard (the `webhook.deliveries.dead_lettered` gauge already counts what it would replay).
- Integration tests: migrate one template database per run and copy it for each test class (a backup and restore, or `CREATE DATABASE ... AS SNAPSHOT` where the edition allows), instead of migrating 30-odd databases; the per-class setup is now about 3.5 s and the suite about 60 s, and it grows with every migration.
- Update this machine's .NET SDK past 10.0.101: CI runs 10.0.403, whose analyzers caught an unused using the local SDK missed. [USER-GATED: installing an SDK changes the machine; the owner's call.]
- Five Dependabot pull requests (#1 to #5) from the first scan: #2 groups minor and patch bumps and can merge once CI is green; #1, #3, #4, and #5 are majors the new ignore rules now hold back. [USER-GATED: merging or closing them is the owner's call.]

## Later: parked

- **Python reconciliation export** for a Power BI dataset. [BLOCKED: only if the settlement work (T24) shows a pipeline is needed; the brief says Python only if needed.]
- **Azure DevOps pipeline** mirroring the GitHub Actions gates. [USER-GATED: an Azure DevOps organization to run it in; an unrun pipeline would be unverified.]
- **Real Azure deployment.** [USER-GATED: never for this repository; portfolio rule is validate only.]
