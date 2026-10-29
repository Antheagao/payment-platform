# Future

Prioritized work ahead, with the reasoning.
Move items to `past.md` as they land and update `present.md`.
Task IDs match the task board in `present.md`.
This file is also the queue for the `batch-loop` skill (`.claude/skills/batch-loop/SKILL.md`): rows are taken top to bottom, and a `[USER-GATED: ...]` or `[BLOCKED: ...]` tag means skip it until the condition after the colon is met.

Last updated: 2026-10-28.

The scaffold (T1 to T18) is complete; its history is in `past.md`.
The scaffold run stopped here on purpose: the owner asked for the initial structure, and the payment slice below starts the features.

## Now: the payment slice

Ranked by what the exercise grades most: the lifecycle and its audit trail, idempotency, the async workflow, then the dashboard.

### T19. Merchant accounts and login

First slice, ready to start:
- Migration `InitialSchema`: `Merchants` and `MerchantUsers` (email `nvarchar(254)` unique under the default case-insensitive collation, password hash, created time), and `READ_COMMITTED_SNAPSHOT ON` to match Azure SQL (database overlay).
- `PaymentsDbContext.OnModelCreating` gains `ApplyConfigurationsFromAssembly` with the first `IEntityTypeConfiguration` (it was removed while there were none, because EF Core warns on every start; `StartupTests` keeps a healthy start free of warnings).
- `Application/Auth/LogIn.cs`: ASP.NET Core Identity's `PasswordHasher`, a dummy-hash comparison for unknown emails, one "email or password is incorrect" error, a JWT per ADR 004 (HS256 pinned, 30 minutes, issuer, audience, `sub`, `merchant_id`, `jti`).
- `Api/Endpoints/AuthEndpoints.cs`: `POST /api/v1/auth/login`, rate limited per email and per address with `Retry-After`; JwtBearer validation registered for the `/api/v1` group.
- A development seed (one demo merchant and user) applied with the startup migration, never in production.
- Gate: integration tests for success, wrong password, unknown email (same response and similar timing), malformed body, rate limit, an expired token, a tampered token, and a token with another algorithm; the gates in `AGENTS.md`.

### Then

- **T20. Create and read payments.** Payments and payment-event (audit) tables, `POST /api/v1/payments` with `Idempotency-Key`, `GET /api/v1/payments/{id}`, merchant scoping (another merchant's payment is a 404), card tokenization at the edge with the simulated vault, the simulated authorization, and the log-capture test that proves no card number or CVV is logged.
- **T21. Capture and refund.** `POST /payments/{id}/capture` and `/refund`, idempotent, every transition audited with actor, time, and correlation id.
- **T22. List and search.** Filters by status, date range, amount, and reference; keyset pagination with an opaque cursor; the indexes that serve it.
- **T23. Outbox and webhook delivery.** Outbox rows written in the same transaction as the state change, a dispatcher claiming rows with `READPAST`, HMAC-signed webhooks, exponential backoff with jitter, a dead-letter state, SSRF rules for merchant URLs, and trace context carried across the hop; the worker's first integration tests.
- **T24. Settlement.** A scheduled batch that settles captured payments through a T-SQL stored procedure, with reconciliation counts.
- **T25. Domain and queue metrics.** Payments created, captured, refunded, failed by state; outbox depth, oldest-pending lag, retries, dead letters.
- **T26. Dashboard login.** Login form, the JWT held in an HttpOnly cookie on the Next.js server, sign-out, expiry handling, and a nonce-based CSP from `proxy.ts`.
- **T27. Payments list.** Filters and search bound to the URL, pagination, empty and error states.
- **T28. Payment detail.** Amounts, status, and the audit timeline.
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
