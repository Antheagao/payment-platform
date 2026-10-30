# Future

Prioritized work ahead, with the reasoning.
Move items to `past.md` as they land and update `present.md`.
Task IDs match the task board in `present.md`.
This file is also the queue for the `batch-loop` skill (`.claude/skills/batch-loop/SKILL.md`): rows are taken top to bottom, and a `[USER-GATED: ...]` or `[BLOCKED: ...]` tag means skip it until the condition after the colon is met.

Last updated: 2026-10-29.

The scaffold (T1 to T18), merchant login (T19), creating and reading payments (T20), capture and refund (T21), list and search (T22), the transactional outbox (T23a), the webhook endpoint API (T23b), signed webhook delivery (T23c), settlement (T24), the domain and queue metrics (T25), the dashboard's sign-in (T26), the payments list (T27), the payment detail (T28), the client's address per person (T26b), the golden path through the composed stack (T29), traces from the dashboard's server (T29b), the load test (T31), the documentation checked against the code (T30), and the reconciliation of payments left `Pending` (T41) are complete; their history is in `past.md`.
The owner asked on 2026-10-28 for the loop to run continuously through the requirements, each checked for correctness against the code, not only the docs.

## Now: the payment slice

Ranked by what the exercise grades most: the lifecycle and its audit trail, idempotency, the async workflow, then the dashboard.

### T26c. Platform staff: their own identity in the API

The owner chose on 2026-10-29: platform operators are staff with an identity of their own, apart from merchant users (over a role on a merchant user, or rewording the panel). Write it down as ADR 011 first.
- **Domain and data.** `StaffUser` (`Domain/Staff/`: id, email, password hash, created at), table `StaffUsers` (migration `StaffUsers`), the same hasher and plausibility rules as `MerchantUser`; no merchant id, because staff cross merchants. Seeded in compose as `operator@example.com` / `LocalOnly_Operator_2026!` (the demo seed, refused in Production). In Azure staff sign in with Entra ID instead (ADR only; not built).
- **Sign-in.** `POST /api/v1/staff/auth/login`, the same limits per email and per address (ADR 010), a token with `sub` the staff id, a `role` claim `staff`, and no `merchant_id`; the default policy already refuses such a token on every merchant route (`403`), and a new `staff` policy requires the role and refuses a merchant's token. `GET /api/v1/staff/me`.
- **The dead-letter replay** (ADR 003): `GET /api/v1/staff/webhook-deliveries?status=dead_lettered` (keyset paged, newest first, with merchant, event type, attempts, last error) and `POST /api/v1/staff/webhook-deliveries/{id}/replay`, which puts a dead-lettered delivery back to `Pending` with its attempts reset and due now, idempotent (a delivery not dead-lettered is `409 delivery_not_dead_lettered`), logged with the staff id, and counted.
- Gate: integration tests for the login (every refusal the merchant login has), the token's claims, a staff token on a merchant route and a merchant token on a staff route (`403` both ways), the list and the replay (delivered on the worker's next run), the replay's `409`, and no secret in a log line; `docs/demo.md` with the curl steps; the security, api-design, and architecture overlays.

### T26d. The operator view in the dashboard

- `/operator/login` signs staff in (its own cookie, `pp_staff_session`, same flags; ADR 004), and `/operator` shows the service status panel (moved from the home page), with the header's API badge, and the dead-lettered deliveries with **Replay**.
- A merchant's home page loses the panel and shows one "payments are delayed" banner when the API reports not ready; the header's API badge shows for staff only.
- The proxy sends `/operator/*` without a staff cookie to `/operator/login`; a merchant cookie opens nothing there, and a staff cookie opens no merchant page.
- Gate: Vitest for the banner and the staff session; Playwright against the stub for both kinds of sign-in, the operator page, the replay, and the banner; the composed walk-through as each; the capture script signs in as the operator for the status panel, the outage GIF, and the stills; `docs/demo.md`, the README images, and the tradeoffs row on the status panel.

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
