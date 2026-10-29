# Future

Prioritized work ahead, with the reasoning.
Move items to `past.md` as they land and update `present.md`.
Task IDs match the task board in `present.md`.
This file is also the queue for the `batch-loop` skill (`.claude/skills/batch-loop/SKILL.md`): rows are taken top to bottom, and a `[USER-GATED: ...]` or `[BLOCKED: ...]` tag means skip it until the condition after the colon is met.

Last updated: 2026-10-29.

The scaffold (T1 to T18), merchant login (T19), creating and reading payments (T20), capture and refund (T21), list and search (T22), the transactional outbox (T23a), the webhook endpoint API (T23b), signed webhook delivery (T23c), settlement (T24), and the domain and queue metrics (T25) are complete; their history is in `past.md`.
The owner asked on 2026-10-28 for the loop to run continuously through the requirements, each checked for correctness against the code, not only the docs.

## Now: the payment slice

Ranked by what the exercise grades most: the lifecycle and its audit trail, idempotency, the async workflow, then the dashboard.

### T26. Dashboard sign-in

R10 (ADR 004): the merchant dashboard's login, the first page behind it, and the security headers the brief's frontend needs. Load the `payment-platform-frontend-ui` and `payment-platform-security` overlays, then the global guides, and read `frontend/node_modules/next/dist/docs/` for the installed Next.js 16 before using an API.
- **Sign-in.** A `/login` page (email and password, `useActionState`, labels, `autoComplete`, errors by `code` from the API's problem details, the demo login shown only in Development); a Server Action that calls `POST /api/v1/auth/login` from the Next.js server and keeps the token in an `HttpOnly`, `Secure` outside Development, `SameSite=Lax` cookie that expires with the token; sign-out clears it; an expired or refused token sends the browser back to `/login` with the page it wanted.
- **Guard.** `proxy.ts` sends a request without the cookie to `/login` (optimistic only); every server read checks the session itself, and the API's `401` is handled where the data is read.
- **Headers.** A nonce-based Content Security Policy set in `proxy.ts` (no `unsafe-inline` for scripts), the existing security headers kept.
- **Home page.** The service status panel stays for an operator; a merchant sees a single banner when payments are delayed (docs/tradeoffs.md), driven by the API's readiness.
- Gate: Vitest for the action and the cookie rules; Playwright for sign-in, a wrong password, sign-out, an expired token, and the CSP header at 1440 and 360; the browser walk-through in both schemes; `docs/images/` remade; `docs/demo.md` signs in.

### Then

- **T27. Payments list.** Filters and search bound to the URL, pagination, empty and error states.
- **T28. Payment detail.** Amounts, status, and the audit timeline.
- Every feature row also updates `docs/demo.md` with the step that shows it, verified against the composed stack, and a row that changes what a page shows remakes `docs/images/` with `node scripts/capture-screenshots.mjs`.
- **T29. End-to-end golden path.** Playwright: log in, find a payment, open its timeline; CI job against the composed stack.
- **T30. Documentation complete.** Tradeoffs, assumptions, production considerations, and future improvements rewritten against what was actually built.
- **T31. Load test.** A k6 script in a container against the composed stack, with the numbers and the bottleneck written down.

## Smaller items to fold in

- A replay for dead-lettered webhook deliveries (ADR 003): `POST` on a delivery that puts it back to `Pending` with its attempts reset, for an operator role; fold into T26, which brings roles to the dashboard (the `webhook.deliveries.dead_lettered` gauge already counts what it would replay).
- Integration tests: migrate one template database per run and copy it for each test class (a backup and restore, or `CREATE DATABASE ... AS SNAPSHOT` where the edition allows), instead of migrating 30-odd databases; the per-class setup is now about 3.5 s and the suite about 60 s, and it grows with every migration.
- OpenTelemetry in the dashboard (`instrumentation.ts`) so traces start at the browser request; fold into T26.
- Update this machine's .NET SDK past 10.0.101: CI runs 10.0.403, whose analyzers caught an unused using the local SDK missed. [USER-GATED: installing an SDK changes the machine; the owner's call.]
- Five Dependabot pull requests (#1 to #5) from the first scan: #2 groups minor and patch bumps and can merge once CI is green; #1, #3, #4, and #5 are majors the new ignore rules now hold back. [USER-GATED: merging or closing them is the owner's call.]

## Later: parked

- **Python reconciliation export** for a Power BI dataset. [BLOCKED: only if the settlement work (T24) shows a pipeline is needed; the brief says Python only if needed.]
- **Azure DevOps pipeline** mirroring the GitHub Actions gates. [USER-GATED: an Azure DevOps organization to run it in; an unrun pipeline would be unverified.]
- **Real Azure deployment.** [USER-GATED: never for this repository; portfolio rule is validate only.]
