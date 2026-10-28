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
- Containers: chiseled runtime images (no shell, no package manager, non-root); compose binds every port to 127.0.0.1; the app connects as `payments_app` (one database) and only the bootstrap uses `sa`; passwords come from the gitignored `.env`.
- Secrets: none in the repository; the API reads `ConnectionStrings:Payments` from the environment or user secrets (`UserSecretsId` is set), and the EF design-time factory holds no credentials.

## Card data (PCI mindset)

- The API tokenizes a card number the moment it arrives and keeps only the token, brand, last four digits, and expiry; the CVV is used once for the simulated authorization and never stored (T20).
- Card numbers, CVVs, tokens, passwords, and signing keys never appear in logs, traces, problem details, idempotency records, or outbox payloads; request bodies are never logged.
- T20 adds a test that captures every log line of a payment flow and fails on any card number or CVV.
- Only the simulator's test card numbers are ever used; a real card number in a test, fixture, or screenshot is a defect.

## Planned, with the task that lands it

- T19: JWT bearer validation (signature, issuer, audience, lifetime, HS256 pinned), passwords with ASP.NET Core Identity's `PasswordHasher`, a dummy hash for unknown emails, login rate limits with `Retry-After`, and no-token and wrong-merchant tests on every protected route.
- T23: webhook URLs are merchant-supplied, so delivery is an SSRF surface: `https` only, resolve the host and refuse private, loopback, and link-local ranges, follow no redirects, 10 s timeout, and sign every body with HMAC-SHA256 over the timestamp and raw bytes.
- T16: CodeQL for C# and TypeScript, Dependabot, and `npm audit --omit=dev` in CI.

## Review checklist additions

- Could this value contain card data, a token, or a secret, and where does it go: a log, a span, a response, a stored payload?
- Does this outbound call go to a URL a merchant controls?
