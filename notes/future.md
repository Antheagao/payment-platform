# Future

Prioritized work ahead, with the reasoning.
Move items to `past.md` as they land and update `present.md`.
Task IDs match the task board in `present.md`.
This file is also the queue for the `batch-loop` skill (`.claude/skills/batch-loop/SKILL.md`): rows are taken top to bottom, and a `[USER-GATED: ...]` or `[BLOCKED: ...]` tag means skip it until the condition after the colon is met.

Last updated: 2026-10-28.

## Now: scaffold

The goal of this phase is a walking skeleton: every project exists, builds, is wired for observability, and is covered by the gates CI runs, before any payment feature lands.
Each row is one commit and one push; a guide overlay in `.claude/skills/` lands with the code it describes, so the overlays only ever state facts.

### T16. Continuous integration

- `.github/workflows/ci.yml`: backend (format, build, tests with SQL Server), frontend (typegen, tsc, eslint, tests, build, audit), compose validation; `dependabot.yml`; CodeQL for C# and TypeScript.
- Overlay: `payment-platform-ci-cd`.
- Gate: CI green on the pushed commit.

### T17. Infrastructure as code

- `infra/bicep/`: App Service for the API and the dashboard, the worker host, Azure SQL, Service Bus, Storage, Key Vault, Application Insights, all with managed identities.
  Validated with `az bicep build` and `bicep lint` in CI; never deployed (portfolio rule: no real deploys).
- Gate: the CI infra job is green.

### T18. README and documentation deliverables

- README: what it is, how to run it locally, the architecture in one diagram, links to docs.
- `docs/`: tradeoffs, assumptions, production considerations, future improvements, each honest about what is built and what is planned.
- Gate: links resolve; a fresh clone follows the setup steps successfully.

## Next: the payment slice

Ranked by what the exercise grades most: the lifecycle and its audit trail, idempotency, the async workflow, then the dashboard.

- **T19. Merchant accounts and login.** Merchants and users tables (first migration), password hashing, `POST /api/v1/auth/login` issuing a short-lived signed JWT, login rate limiting, a seeded demo merchant, and tests for expiry and tampering.
- **T20. Create and read payments.** Payments and payment-event (audit) tables, `POST /api/v1/payments` with `Idempotency-Key`, `GET /api/v1/payments/{id}`, merchant scoping (another merchant's payment is a 404), the simulated authorization.
- **T21. Capture and refund.** `POST /payments/{id}/capture` and `/refund`, idempotent, every transition audited with actor, time, and correlation id.
- **T22. List and search.** Filters by status, date range, amount, and reference; keyset pagination with an opaque cursor; the indexes that serve it.
- **T23. Outbox and webhook delivery.** Outbox rows written in the same transaction as the state change, a dispatcher claiming rows with `READPAST`, HMAC-signed webhooks, exponential backoff with jitter, a dead-letter state, and trace context carried across the hop.
- **T24. Settlement.** A scheduled batch that settles captured payments through a T-SQL stored procedure, with reconciliation counts.
- **T25. Domain and queue metrics.** Payments created, captured, refunded, failed by state; outbox depth, oldest-pending lag, retries, dead letters.
- **T26. Dashboard login.** Login form, the JWT held in an HttpOnly cookie on the Next.js server, sign-out, expiry handling.
- **T27. Payments list.** Filters and search bound to the URL, pagination, empty and error states.
- **T28. Payment detail.** Amounts, status, and the audit timeline.
- **T29. End-to-end golden path.** Playwright: log in, find a payment, open its timeline; CI job against the composed stack.
- **T30. Documentation complete.** Tradeoffs, assumptions, production considerations, and future improvements rewritten against what was actually built.
- **T31. Load test.** A k6 script in a container against the composed stack, with the numbers and the bottleneck written down.

## Later: parked

- **Python reconciliation export** for a Power BI dataset. [BLOCKED: only if the settlement work (T24) shows a pipeline is needed; the brief says Python only if needed.]
- **Azure DevOps pipeline** mirroring the GitHub Actions gates. [USER-GATED: an Azure DevOps organization to run it in; an unrun pipeline would be unverified.]
- **Real Azure deployment.** [USER-GATED: never for this repository; portfolio rule is validate only.]
