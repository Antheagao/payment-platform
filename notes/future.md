# Future

Prioritized work ahead, with the reasoning.
Move items to `past.md` as they land and update `present.md`.
Task IDs match the task board in `present.md`.
This file is also the queue for the `batch-loop` skill (`.claude/skills/batch-loop/SKILL.md`): rows are taken top to bottom, and a `[USER-GATED: ...]` or `[BLOCKED: ...]` tag means skip it until the condition after the colon is met.

Last updated: 2026-10-29.

The scaffold (T1 to T18), merchant login (T19), creating and reading payments (T20), capture and refund (T21), list and search (T22), the transactional outbox (T23a), the webhook endpoint API (T23b), signed webhook delivery (T23c), settlement (T24), the domain and queue metrics (T25), the dashboard's sign-in (T26), the payments list (T27), the payment detail (T28), the client's address per person (T26b), the golden path through the composed stack (T29), traces from the dashboard's server (T29b), the load test (T31), the documentation checked against the code (T30), the reconciliation of payments left `Pending` (T41), platform staff as their own identity in the API (T26c), the operator area in the dashboard (T26d), and the measurement that closed T42 are complete; their history is in `past.md`.
The owner asked on 2026-10-28 for the loop to run continuously through the requirements, each checked for correctness against the code, not only the docs.

## Now

Every requirement in `docs/requirements.md` is done, and so is every feature row; the queue holds no workable row and nothing waits on the owner; what is left is the parked rows below. A new row starts from `docs/future-improvements.md` or the owner's next request.

Every feature row also updates `docs/demo.md` with the step that shows it, verified against the composed stack, and a row that changes what a page shows remakes `docs/images/` with `node scripts/capture-screenshots.mjs`.

## Later: parked

- **Python reconciliation export** for a Power BI dataset. [BLOCKED: only if the settlement work (T24) shows a pipeline is needed; the brief says Python only if needed.]
- **Azure DevOps pipeline** mirroring the GitHub Actions gates. [USER-GATED: an Azure DevOps organization to run it in; an unrun pipeline would be unverified.]

## Not planned

- **A real Azure deployment.** This repository is a job application's technical exercise: the Bicep is built, linted, and parameter-checked in CI and never deployed, and `infra/README.md` lists what a deployment would add (the owner, 2026-10-29).
