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
  Read `01-app/02-guides/authentication-with-cache-components.md` before building sign-in (T26).
- **The dashboard follows the operating system's color scheme** (`color-scheme: light dark` on `:root`); there is no theme toggle yet, so `light-dark()` tokens are the only theming and `dark:` is never used.
- **Port 3080**, not 3000 (`npm run dev`, `npm start`, compose), because 3000 belongs to other projects on this machine.

## What exists

- `src/lib/api.ts`: `import "server-only"`; reads the server-only `API_ORIGIN` (default `http://localhost:5080`); `getApiHealth()` never throws, uses `cache: "no-store"` and a 3 s `AbortSignal.timeout`.
  New API calls go here (or in `src/lib/<feature>-api.ts` when it grows) and parse problem details in one place, switching on `code`.
- `src/lib/payment-status.ts`: the `PaymentStatus` union as the API spells it, `paymentStatusDisplay` (label, tone, meaning) as a `Record` over the union, and `lifecycleOrder`.
- `src/components/ui/`: `Badge` (tone pill with a dot and a text label), `Button` and `buttonClasses()` (primary, secondary; `type="button"` unless overridden), `Container` (the one page column).
- `src/components/`: `SiteHeader` (sticky, product mark, `ApiStatus` in `<Suspense>`), `ApiStatus` and `ApiStatusFallback`, `PaymentLifecycle` (the status legend).
- `src/app/`: root layout with Geist, the skip link, and `<main id="main">`; `page.tsx`; `error.tsx` (client, shows the digest, `retry()`); `global-error.tsx` (own `<html>` and `<body>`, imports the tokens); `not-found.tsx`.
- `next.config.ts`: standalone output only when `NEXT_OUTPUT=standalone` (the Dockerfile sets it; `next start` does not support standalone), no `X-Powered-By`, and headers on every response: `frame-ancestors 'none'`, `object-src 'none'`, `base-uri 'self'`, `nosniff`, `X-Frame-Options: DENY`, `strict-origin-when-cross-origin`, a restrictive `Permissions-Policy`.
- `frontend/Dockerfile`: `npm ci` and `next build` on Node 22, then the standalone server on distroless `nodejs22-debian12:nonroot`; compose runs it with `API_ORIGIN=http://api:8080`.

## Design tokens

- Tokens live only in `src/app/globals.css`: neutrals on hue 260, indigo (265) as the single accent for the main action, focus, and captured; teal (200) info for authorized; green success, amber warning, red danger.
- `node scripts/check-contrast.mjs` reads that file and fails on any token outside sRGB or any pair under its target (body 7:1, secondary text and every pill 4.5:1, borders and focus 3:1).
  Adding a token means the `:root` value, the `@theme inline` mapping, a pair in the script's `PAIRS`, and this list, in one change.
- The neutral pill has an inset ring because it sits on `surface-2` rows.

## Known gaps

- No script-src CSP yet: a nonce needs `proxy.ts` and per-request rendering, which arrives with sign-in (T26).
- No OpenTelemetry in the dashboard yet, so traces start at the API; `instrumentation.ts` with OTLP export is a follow-up.

## Visual QA routine

- `docker compose up -d --build`, then `chrome-devtools-axi` with `CHROME_DEVTOOLS_AXI_SESSION=payment-platform`: `open http://localhost:3080/`, `emulate --viewport 1440x900x1 --color-scheme light`, then dark, then `360x780x2,mobile` in both, `screenshot` each, and `eval "document.documentElement.scrollWidth + ' / ' + document.documentElement.clientWidth"` for horizontal overflow.
- Check the API-down state (`docker compose stop api`) and the first `Tab` (the skip link); `chrome-devtools-axi stop` and `docker compose down` afterwards.
