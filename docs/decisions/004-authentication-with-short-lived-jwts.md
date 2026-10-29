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

## Amendments

- 2026-10-28, as built (T19): the API, not the login use case, issues the token (`Api/Auth/TokenIssuer.cs`); `Application/Auth/LogIn.cs` only decides who the caller is, so the token format stays with the rest of HTTP authentication.
  The key is `Jwt:SigningKey` (`Jwt__SigningKey` from Key Vault in Azure); with none set, Development generates one at startup and every other environment refuses to start, so no key is ever committed.
  `GET /api/v1/me` returns the caller for the dashboard; a token whose user no longer exists, or no longer belongs to its merchant, is `401`; a genuine token without both ids is `403`.
  The per-address login limit is 60 a minute because every dashboard login reaches the API from the dashboard's server; it becomes per person when the dashboard forwards the client address from a known proxy (T26).
- 2026-10-29, the dashboard's side as built (T26): `/login` posts to a Server Action (`app/login/actions.ts`) that calls the API from the Next.js server and keeps the token in the `pp_session` cookie, `HttpOnly`, `Secure`, `SameSite=Strict`, for the token's lifetime (`lib/session.ts`); sign-out deletes it.
  `src/proxy.ts` sends a browser with no cookie to `/login?next=...`, and the action returns only to a path on this site (`lib/redirect-target.ts`); the header asks `/me` for the merchant's name and, for a refused or expired token, offers sign-in again rather than redirecting, because the header is on the login page too.
  Forwarding the client's address, so the per-address login limit applies per person, is still to come (T26b).
- 2026-10-29, the client's address (T26b): the dashboard names each person's address to the API under a key the two share, and the per-address limit is now 20 a minute per person; see [ADR 010](010-client-address-from-the-dashboard.md).
