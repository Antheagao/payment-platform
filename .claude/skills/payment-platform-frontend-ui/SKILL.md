---
name: payment-platform-frontend-ui
description: Payment Platform dashboard facts - Cache Components and Suspense rules, the server-only API module, design tokens and the contrast gate, UI primitives, status display, the container image, and the visual QA routine. Use when building or reviewing a page, component, style, data fetch, or error state in frontend/.
---

# Payment Platform frontend-ui overlay

Load the global `frontend-ui` guide for the rules; this file says what exists here and where this app differs.
Read `frontend/node_modules/next/dist/docs/` before any Next.js API: the installed version is 16.4.3.

## Local exceptions to the global guide

- **Cache Components is on** (`cacheComponents` and `partialPrefetching`, create-next-app's default and the only mode in the next major), so the global guide's "fetch is uncached by default with cacheComponents off" does not apply.
  Data is dynamic unless marked `"use cache"`; every component that awaits an uncached fetch or `cookies()` renders inside `<Suspense>` with a same-size fallback, or the build fails with a blocking-route error.
  Never put per-merchant data behind `"use cache"`.
- **Every page renders per request** (ADR 009): the root layout awaits `connection()` and sets `export const instant = false`, because the nonce-based Content-Security-Policy cannot reach a prerendered shell's scripts (on the default rendering the browser refused 9 of 18 and the page did not hydrate). `<Suspense>` still streams; never reintroduce a static shell, an inline `style` attribute, or a script outside `next/script`.
- **The dashboard follows the operating system's color scheme** (`color-scheme: light dark` on `:root`); there is no theme toggle yet, so `light-dark()` tokens are the only theming and `dark:` is never used.
- **Port 3080**, not 3000 (`npm run dev`, `npm start`, compose), because 3000 belongs to other projects on this machine.

## What exists

- `src/proxy.ts` (ADR 009): a nonce per page request and the policy that requires it (`contentSecurityPolicy`), the nonce and the path passed on as `x-nonce` and `x-pathname`, and a redirect to `/login?next=...` for a browser with no `pp_session` cookie (optimistic only: data reads check the token with the API).
- Sign-in (ADR 004): `src/lib/session.ts` (`readToken`, `saveToken` with `HttpOnly`, `Secure`, `SameSite=Strict` and the token's expiry, `clearToken`), `src/lib/auth-api.ts` (`signIn` and `getCurrentUser`, by problem-details `code`, never throwing), `src/lib/redirect-target.ts` (`safeRedirectTarget`: a path on this site or `/`), `src/app/login/` (`page.tsx`, `actions.ts` with `signInAction` and `signOutAction`), `src/components/login-form.tsx` (`useActionState`, the field at fault with `aria-invalid` and `aria-describedby`, a mounted `role="alert"` that takes no space while empty), and `src/components/user-menu.tsx` (the merchant from `/me` and Sign out; it never redirects, and stays empty on `/login`). `DEMO_LOGIN_HINT=true` (compose) shows the demo login.
- Payments (T27): `src/lib/payments-api.ts` (`requireToken`, which sends a browser without a session to sign in and back; `listPayments`, which turns the API's `401` into a sign-in with `reason=expired`, returns its field codes for a `400`, and throws `PaymentsUnavailableError` for the segment's `error.tsx` otherwise), `src/lib/money.ts` (`formatMoney` and `toMinorUnits` per currency, on digits rather than floating point), `src/lib/payment-filters.ts` (`parseFilters`, `pageHref`), `src/app/payments/` (`page.tsx`, which redirects a request with empty fields to its clean address, `error.tsx`), `src/components/payments/` (`PaymentFiltersForm`, a `GET` form inside a `<details>` that opens when a filter is set or wrong; `PaymentsResults`, a table from `lg` and cards below it, both in the DOM, so tests pick the visible one), `src/components/main-nav.tsx` (the sections beside the brand from `sm` up, `aria-current` on the current one).
- `src/lib/api.ts`: `import "server-only"`; reads the server-only `API_ORIGIN` and `WORKER_ORIGIN` (defaults `http://localhost:5080` and `5081`); `getApiHealth()` and `getWorkerHealth()` are React `cache()`d per request, never throw, `await connection()` before timing the round trip, use `cache: "no-store"` and a 5 s `AbortSignal.timeout` (longer than the 3 s readiness deadline, so a database outage shows as not ready), and keep only the checks the health writer can send.
  New API calls go here (or in `src/lib/<feature>-api.ts` when it grows) and parse problem details in one place, switching on `code`.
- `src/lib/service-status.ts`: `serviceRows()` turns the two readiness answers into the status panel's tiles (the database is the API's `database` check, and unknown, never down, when the API does not answer) and `pendingServiceRows` for the fallback.
- `src/lib/payment-status.ts`: the `PaymentStatus` union as the API spells it, `paymentStatusDisplay` (label, tone, meaning) as a `Record` over the union, and `lifecycleOrder`.
- `src/components/ui/`: `Badge` (tone pill with a dot and a text label), `Button` and `buttonClasses({ variant, size, className })` (primary, secondary; `md` 40px and `sm` 32px; `type="button"` unless overridden), `Container` (the one page column).
- `src/components/`: `SiteHeader` (sticky, product mark, `MainNav`, `ApiStatus` from `sm` up and `UserMenu`, each in `<Suspense>`; below `sm` it holds only the brand and the session, because they no longer fit at 360px), `ApiStatus` and `ApiStatusFallback`, `ServiceStatus` and `ServiceStatusFallback` (three tiles, same size while streaming), `LiveUpdates` (client; `router.refresh()` every 5 s while the tab is visible, in a transition, skipping a tick while the last refresh still runs, because a new one cancels it and during an outage the page then never changed; with Pause, which WCAG 2.2.2 requires of content that updates itself), `PaymentLifecycle` (the four steps as a flow with arrows, stacked below `lg`, and the two terminal branches under the step they leave).
- `src/app/`: root layout with Geist, the skip link, and `<main id="main">`; `page.tsx` (the heading, the service status section, the lifecycle); `error.tsx` (client, shows the digest, `retry()`); `global-error.tsx` (own `<html>` and `<body>`, imports the tokens); `not-found.tsx`.
- `next.config.ts`: standalone output only when `NEXT_OUTPUT=standalone` (the Dockerfile sets it; `next start` does not support standalone), no `X-Powered-By`, and headers on every response: `frame-ancestors 'none'`, `object-src 'none'`, `base-uri 'self'`, `nosniff`, `X-Frame-Options: DENY`, `strict-origin-when-cross-origin`, a restrictive `Permissions-Policy`.
- `frontend/Dockerfile`: `npm ci` and `next build` on Node 22, then the standalone server on distroless `nodejs22-debian12:nonroot`; compose runs it with `API_ORIGIN=http://api:8080` and `WORKER_ORIGIN=http://worker:8080`.

## Design tokens

- Tokens live only in `src/app/globals.css`: neutrals on hue 260, indigo (265) as the single accent for the main action, focus, and captured; teal (200) info for authorized; green success, amber warning, red danger.
- `node scripts/check-contrast.mjs` reads that file and fails on any token outside sRGB or any pair under its target (body 7:1, secondary text and every pill 4.5:1, borders and focus 3:1).
  Adding a token means the `:root` value, the `@theme inline` mapping, a pair in the script's `PAIRS`, and this list, in one change.
- The neutral pill has an inset ring because it sits on `surface-2` rows.
- The six status pills are also held to one contrast band per scheme (strongest over weakest at most 1.25, `PILLS` in the script), so no status reads louder than another by accident; dark primary and danger were raised from 5.9 and 6.3 to 7.5 and 7.6 to meet it.

## Known gaps

- No OpenTelemetry in the dashboard yet, so traces start at the API; `instrumentation.ts` with OTLP export is a follow-up.

## Visual QA routine

- `docker compose up -d --build`, then `chrome-devtools-axi` with `CHROME_DEVTOOLS_AXI_SESSION=payment-platform`: `open http://localhost:3080/`, `emulate --viewport 1440x900x1 --color-scheme light`, then dark, then `360x780x2,mobile` in both, `screenshot` each, and `eval "document.documentElement.scrollWidth + ' / ' + document.documentElement.clientWidth"` for horizontal overflow.
- Signed-in pages need a session: sign in through the form (the capture script signs in once and shares the storage state, because the API allows five sign-ins per email in five minutes). The end-to-end tests run against `e2e/stub-api.mjs`, which answers sign-in and `/me` and drops health probes.
- Check the database-down and API-down states (`docker compose stop sqlserver`, then `api`; the panel follows within 5 s) and the first `Tab` (the skip link); `chrome-devtools-axi stop` and `docker compose down` afterwards.
- Then remake the README and demo images: `node scripts/capture-screenshots.mjs` (Playwright from `frontend/`, FFmpeg for `outage.gif`), and look at every file it writes; Aspire's "Endpoint is unsecured" banner is dismissed by class, because its `fluent-button` matches no role query.
