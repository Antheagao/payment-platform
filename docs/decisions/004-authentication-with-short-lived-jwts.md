# 004. Authentication with short-lived JWTs, held by the dashboard's server

- Status: accepted
- Date: 2026-10-28

## Context

Payment endpoints must only be usable by a logged-in merchant user, and the exercise asks how a request is validated and how a login expires.
The API should stay stateless so it scales horizontally, and the token must be out of reach of script running in the browser.

## Decision

- **Login:** `POST /api/v1/auth/login` takes an email and password, verifies the password with ASP.NET Core Identity's `PasswordHasher` (PBKDF2-HMAC-SHA512, 100,000 iterations), and returns a JWT plus its expiry.
  Unknown emails are verified against a fixed dummy hash, so response time does not reveal which accounts exist, and every failure says "email or password is incorrect".
- **The token:** signed with HMAC-SHA256 using a key of at least 256 bits from configuration (Key Vault in Azure); claims `sub` (user id), `merchant_id`, `iat`, `exp`, `jti`, issuer, and audience.
- **Expiry:** 30 minutes, absolute, no refresh token.
  When it expires the API answers `401` with code `token_expired` and the dashboard sends the user to the login page.
- **Validation on every request** (`JwtBearer`): signature, issuer, audience, and lifetime with a 30-second clock skew; the algorithm is pinned to HS256 so a token cannot pick its own.
  Endpoints read the merchant id from the validated claims, never from the request.
- **The dashboard as a backend for the frontend:** the Next.js server performs the login call, stores the token in an `HttpOnly`, `Secure`, `SameSite=Strict` cookie whose lifetime matches the token, and attaches it as a bearer token when its server code calls the API.
  Browser code never sees the token, and the API is never called from the browser.
- **Abuse:** login is rate limited per email and per client address with ASP.NET Core rate limiting, answering `429` with `Retry-After`.

## Rejected alternatives

- **Server-side sessions in a table:** easy to revoke, but every request becomes a database lookup and the API is no longer stateless; with a 30-minute token the revocation window is acceptable for this exercise.
- **Refresh tokens:** they lengthen sessions safely but need rotation, reuse detection, and storage; a future improvement.
- **The token in browser storage:** readable by any injected script.
- **Cookie authentication on the API itself:** works, but couples the API to one browser origin and complicates the server-to-server clients a payment API also serves.
- **Microsoft Entra ID or another OpenID Connect provider:** the right production answer for staff and enterprise merchants, and the JWT validation code would barely change; out of scope for a self-contained exercise.
- **RS256 asymmetric signing:** worth it once other services validate tokens without being able to issue them; with one issuer that is also the only validator, a symmetric key is simpler.

## Consequences

- Logging out deletes the dashboard cookie; a copied token stays valid until it expires, which is the tradeoff of statelessness.
  If revocation becomes a requirement, a per-user security stamp checked on each request is the smallest step.
- The signing key is a secret with a rotation story: accept the current and previous key during a rotation window.
