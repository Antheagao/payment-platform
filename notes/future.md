# Future

Prioritized work ahead, with the reasoning.
Move items to `past.md` as they land and update `present.md`.
Task IDs match the task board in `present.md`.
This file is also the queue for the `batch-loop` skill (`.claude/skills/batch-loop/SKILL.md`): rows are taken top to bottom, and a `[USER-GATED: ...]` or `[BLOCKED: ...]` tag means skip it until the condition after the colon is met.

Last updated: 2026-10-29.

The scaffold (T1 to T18), merchant login (T19), creating and reading payments (T20), capture and refund (T21), list and search (T22), the transactional outbox (T23a), the webhook endpoint API (T23b), signed webhook delivery (T23c), settlement (T24), the domain and queue metrics (T25), the dashboard's sign-in (T26), the payments list (T27), the payment detail (T28), the client's address per person (T26b), the golden path through the composed stack (T29), traces from the dashboard's server (T29b), the load test (T31), the documentation checked against the code (T30), the reconciliation of payments left `Pending` (T41), platform staff as their own identity in the API (T26c), and the operator area in the dashboard (T26d) are complete; their history is in `past.md`.
The owner asked on 2026-10-28 for the loop to run continuously through the requirements, each checked for correctness against the code, not only the docs.

## Now

Every requirement in `docs/requirements.md` is done, and so is every feature row; what is left is one engineering row, and the rows only the owner can unblock.

### T42. Integration tests on one migrated template database

The suite migrates 30-odd databases, one per test class, about 3.5 s each before its first test; the integration tests take about a minute and grow with every migration.
- Migrate one template database per run in `SqlServerFixture`, and give each test class a copy (a backup and restore, or `CREATE DATABASE ... AS SNAPSHOT` where the edition allows), checking first which of the two SQL Server 2025 in Testcontainers supports and how long each takes.
- Gate: the whole suite green three runs in a row, no container left behind, the time before and after written down, and the startup tests that migrate on purpose still migrating an empty database themselves.

Every feature row also updates `docs/demo.md` with the step that shows it, verified against the composed stack, and a row that changes what a page shows remakes `docs/images/` with `node scripts/capture-screenshots.mjs`.

## Waiting on the owner

- Update this machine's .NET SDK past 10.0.101: CI's 10.0.4xx analyzers caught an unused using, and the container build a `CA1873`, that the local SDK missed. [USER-GATED: installing an SDK changes the machine; the owner's call.]
- Five Dependabot pull requests (#1 to #5) from the first scan: #2 groups minor and patch bumps and can merge once CI is green; #1, #3, #4, and #5 are majors the new ignore rules now hold back. [USER-GATED: merging or closing them is the owner's call.]

## Later: parked

- **Python reconciliation export** for a Power BI dataset. [BLOCKED: only if the settlement work (T24) shows a pipeline is needed; the brief says Python only if needed.]
- **Azure DevOps pipeline** mirroring the GitHub Actions gates. [USER-GATED: an Azure DevOps organization to run it in; an unrun pipeline would be unverified.]
- **Real Azure deployment.** [USER-GATED: never for this repository; portfolio rule is validate only.]
