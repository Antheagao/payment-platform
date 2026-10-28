# Dashboard

The merchant dashboard: Next.js 16 (App Router), React 19, TypeScript, Tailwind CSS v4.
It talks to the API only from server code; the browser never holds the API token (ADR 004 in `../docs/decisions/`).

```sh
npm ci
npm run dev        # http://localhost:3080
npm run typecheck && npm run lint && npm run format:check && npm run build
```

Next.js 16 differs from older versions; read `node_modules/next/dist/docs/` before using a Next.js API.
