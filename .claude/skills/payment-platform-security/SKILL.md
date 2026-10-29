---
name: payment-platform-security
description: Payment Platform security facts and gaps - trust boundaries, headers, limits, card data (PCI) handling, secrets, authentication plan, SSRF rules for webhooks, and supply chain. Use when touching input, auth, card data, secrets, headers, outbound HTTP, or dependencies in this repository.
---

# Payment Platform security overlay

Load the global `security` guide for the rules; this file says what exists here, what is planned, and what is specific to payments.
ADR 004 (authentication) and `docs/assumptions.md` (card data) come first.

## Trust boundaries

- Browsers talk only to the Next.js dashboard; its server calls the API with the merchant's bearer token (ADR 004).
- The API has no CORS policy on purpose: no browser origin may call it, and it enforces every rule itself.
- Everything from a request is untrusted, including a caller's `X-Correlation-Id`, which is kept only if it matches a narrow alphabet and is used only for correlation.

## What exists

- `SecurityHeadersMiddleware`: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`, `Referrer-Policy: no-referrer`, `Cache-Control: no-store`; HSTS outside Development; no `Server` header.
- Request bodies capped at 64 KB and requests at 10 s.
- Error responses never carry exception messages (`ProblemDetailsSetup`), and health responses never carry check descriptions or exceptions (`HealthResponseWriter`), with tests for the latter.
- Supply chain: NuGet audit of transitive packages fails the build at any severity; lock files are restored in locked mode in CI; transitive security pins live in `Directory.Packages.props` with the advisory named.
- Containers: chiseled runtime images (no shell, no package manager, non-root); compose binds every port to 127.0.0.1; the app connects as `payments_app` (one database) and only the bootstrap uses `sa`.
- Local passwords: public, local-only defaults in `docker-compose.yml`, so the stack starts with no setup; a gitignored `.env` overrides them. They guard containers reachable only from this machine; Azure has no passwords at all (Entra managed identities, `infra/`), and no other secret may ever get a committed default.
- Authentication (T19, ADR 004): bearer JWTs validated with HS256 pinned, issuer, audience, signature, and lifetime (30 s skew), claims kept under their JWT names (`MapInboundClaims = false`); the default policy on the `/api/v1` group also requires parseable `sub` and `merchant_id` claims; `CallerClaims` is the only way an endpoint learns who is calling.
  Passwords: Identity's `PasswordHasher` (PBKDF2-HMAC-SHA512) behind `PasswordVerifier`, which spends the same work on an unknown email; a weaker stored hash is replaced at the next login.
  Login limits: per email in `LoginThrottle` (5 per 5 minutes, successes included) and per address in the `login-per-address` policy (20 a minute); both answer `429` with `Retry-After`.
  The address is the person's when the dashboard names it in `X-Client-Address` with `X-Dashboard-Key` (`Api/Auth/ClientAddress.cs`, ADR 010), the connection's otherwise; IPv6 by its `/64`. Never read `X-Forwarded-For` in the API, and never trust it in the dashboard beyond the `TRUSTED_PROXY_HOPS` entries its own proxies wrote (`src/lib/client-address.ts`): Next.js keeps a browser's own header as it came.
  The signing key `Jwt:SigningKey` has no committed default: generated per process in Development, a startup failure elsewhere (`SigningKeyTests`).
- Demo login: `demo@example.com` / `LocalOnly_Demo_2026!`, seeded only when `Demo:Seed` is true (compose); the API refuses the setting in Production (`DemoSeedTests`). It is public like the compose passwords, for the same reason.
- Secrets: none in the repository; the API reads `ConnectionStrings:Payments` from the environment or user secrets (`UserSecretsId` is set), and the EF design-time factory holds no credentials.

## Card data (PCI mindset)

- The API tokenizes a card number the moment it arrives and keeps only the token, brand, last four digits, and expiry; the CVV is checked for shape and discarded, never stored ([ADR 008](../../../docs/decisions/008-card-tokenization-at-the-edge.md)).
  `Application/Cards/`: `CardInput` (shape checks, and a `ToString` that masks the number and CVV), `ICardVault`, and `SimulatedCardVault`, which accepts only the published test cards in `TestCards` and refuses any other valid number with `422 card_not_accepted`, so a real card is never stored, not even as four digits.
- Card numbers, CVVs, tokens, passwords, and signing keys never appear in logs, traces, problem details, idempotency records, or outbox payloads; request bodies are never logged.
- `CreatePaymentTests.No_card_number_or_cvv_is_stored_logged_or_returned` captures every log record at Trace and up, every column the create wrote, and the responses, and fails on the number, its grouped form, the CVV, or the token.
- Only the simulator's test card numbers are ever used; a real card number in a test, fixture, or screenshot is a defect.

## Planned, with the task that lands it

- Every new payment route gets the no-token and another-merchant tests that `CreatePaymentTests` and `GetPaymentTests` have.
- Dashboard session (T26): the token lives only in the `pp_session` cookie (`HttpOnly`, `Secure`, `SameSite=Strict`, the token's lifetime), set and read by server code; the login action returns only to a path on this site (`safeRedirectTarget`, tested against protocol-relative, backslash, and control-character targets); a nonce-based Content-Security-Policy on every page with no `'unsafe-inline'` scripts (ADR 009), and the end-to-end tests fail on any violation.
- Webhook URLs are merchant-supplied, so delivery is an SSRF surface. When a URL is set (T23b): `https` only, no user info or fragment, and every address the host resolves to must be public (`WebhookAddresses`: the IANA special-purpose ranges, IPv4-mapped and NAT64 forms judged by the address they carry, and Azure's `168.63.129.16`); a name that mixes public and private addresses is refused. The fake `IHostResolver` keeps the tests off real DNS.
  `Webhooks:LocalReceiverHosts` frees named hosts (the compose receiver) from the rules; both hosts refuse to start with it outside Development.
- At connect time (T23c): the `webhooks` client's `SocketsHttpHandler.ConnectCallback` resolves the host through `WebhookUrlPolicy.ResolveAllowedAsync` and connects only to an allowed address, so a name rebound to a private address after it was set reaches nothing (`HttpWebhookSenderTests`); no proxy, no redirects, no cookies, 5 s to connect and 10 s per attempt, and the response body is never read. Every body is signed in the Standard Webhooks form (HMAC-SHA256 over `id.timestamp.body`, checked against the specification's test vector).
- Webhook signing secrets are stored as issued (signing needs them), shown once, and never logged; `WebhookEndpointTests` checks every log line for them and for a token in the URL's query, which is why the URL is logged by host only.
- T16: CodeQL for C# and TypeScript, Dependabot, and `npm audit --omit=dev` in CI.

## Review checklist additions

- Could this value contain card data, a token, or a secret, and where does it go: a log, a span, a response, a stored payload?
- Does this outbound call go to a URL a merchant controls?
