# 009. A nonce-based Content-Security-Policy, with every dashboard page rendered per request

- Status: accepted
- Date: 2026-10-29

## Context

The dashboard shows a merchant's payments and carries their session, so a script injected into it could act as that merchant.
A Content-Security-Policy that allows only the dashboard's own scripts is the browser's second line against that, behind React's escaping.
The strict form of that policy gives each response a random nonce and runs only scripts that carry it (`script-src 'nonce-...' 'strict-dynamic'`, no `'unsafe-inline'`).

The dashboard has Cache Components on (`cacheComponents`), the Next.js 16 default and its only mode in the next major.
That mode prerenders each page's static shell at build time and streams the per-request parts into it.
A prerendered shell is built before any request exists, so its scripts cannot carry a nonce made for one.
Checked on the production build: with the nonce policy and the default rendering, 11 of the page's 18 scripts lacked the request's nonce, the browser refused 9 of them, and the page did not hydrate.
The installed Next.js guide says the same: Partial Prerendering is incompatible with a nonce-based policy.

## Decision

- **`src/proxy.ts` makes a nonce for every page request** and sends the policy that requires it: `default-src 'self'`; scripts and styles only from this site and only with the nonce, `'strict-dynamic'`, `'unsafe-eval'` in development only (React rebuilds server error stacks with it); `object-src 'none'`, `base-uri 'self'`, `form-action 'self'`, `frame-ancestors 'none'`.
  Next.js reads the nonce from the request's policy and puts it on every script it renders.
- **Every page renders per request.** The root layout waits for the request (`await connection()`) and opts out of instant-navigation validation (`export const instant = false`), so there is no prerendered shell and every script is rendered with the request's nonce.
  The rest of Cache Components stays as it is: `<Suspense>` still streams, and `"use cache"` still works for data that is the same for everyone.
- **The proxy also keeps a browser with no session cookie out of every page but `/login`.**
  That redirect is a convenience, not a check: every read of a merchant's data sends the token to the API, which decides.

## Rejected alternatives

- **`'unsafe-inline'` for scripts**, with the static shell kept: it allows exactly the injected script the policy exists to stop.
- **Subresource Integrity (`experimental.sri`) with a hash-based policy:** it keeps static generation, but it is experimental in Next.js 16 and covers the build's script files, not the inline scripts that carry the page's data.
- **Turning Cache Components off:** it would bring back per-request rendering by default, but against the framework's direction, and the next major has no other mode.
- **No script policy, as before:** the dashboard would lean on React's escaping alone.

## Consequences

- No page is served from a prerendered shell, so each one costs a render on the server.
  Every page after sign-in shows one merchant's data, which a shared shell could not hold anyway; the login page is the only page that could have been static.
- A CDN cannot cache the HTML; the build's static files (`_next/static`) are still immutable and cacheable.
- Any third-party script must load through `next/script` with the nonce, or be loaded by a script that has it (`'strict-dynamic'`).
- Inline `style` attributes are refused by `style-src` without `'unsafe-inline'`; the dashboard styles with classes only.
- The end-to-end tests fail on any policy violation in the console (`e2e/auth.spec.ts`), so a change that breaks the policy breaks the build.
