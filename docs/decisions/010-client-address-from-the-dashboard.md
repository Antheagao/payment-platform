# 010. The client's address reaches the API from the dashboard, under the dashboard's key

- Status: accepted
- Date: 2026-10-29

## Context

The API limits sign-ins per email and per address (ADR 004).
Every person signs in through the dashboard, whose server calls the API, so the API sees one address for everyone: the dashboard's.
The per-address limit was therefore set generously (60 a minute) and limited the dashboard as a whole, not any one person.

To limit per person, the API needs the browser's address, and it must be one no caller can choose.
Two facts, both checked on the running stack, shape how:

- Next.js 16 keeps an `X-Forwarded-For` the browser sends (`x-forwarded-for ??= socket.remoteAddress` in `next/dist/server/base-server.js`); a request sent straight to the dashboard with `X-Forwarded-For: 203.0.113.7` reached `src/proxy.ts` with exactly that value.
  So the header is only worth what the proxy in front of the dashboard writes into it, and a dashboard with no proxy in front cannot tell the person's address from a lie.
- The dashboard is not a proxy in the API's network path: it is a client acting for the person.
  In Azure the API is reached from the internet, and the dashboard's outbound address on App Service is shared and not an identity, so "trust `X-Forwarded-For` from the dashboard's network" (ASP.NET Core's forwarded-headers middleware) has nothing sound to trust.

## Decision

- **The dashboard always sits behind a proxy that writes the address it saw.**
  In compose that is Caddy (`infra/edge/Caddyfile`, the `edge` service), now the only way in on port 3080; it trusts no client as a proxy, so it replaces any `X-Forwarded-For` a browser sends.
  In Azure it is App Service's front end, which appends the address, sometimes with a port; with Front Door ahead of it there are two.
- **The dashboard takes the entry its trusted proxies wrote**: with `TRUSTED_PROXY_HOPS` proxies in front (1 by default), the entry that many places from the right (`src/lib/client-address.ts`).
  Entries further left came from the browser and are ignored; one that is not an address is no address.
- **On sign-in the dashboard names that address to the API** in `X-Client-Address`, with `X-Dashboard-Key`, a secret the two share (`Dashboard:Key` in the API, `DASHBOARD_KEY` in the dashboard; Key Vault in Azure, a public local-only value in compose).
- **The API believes a named address only with the right key** (`Api/Auth/ClientAddress.cs`): compared as SHA-256 hashes in fixed time, one value of each header, and an address that parses.
  Otherwise it limits by the connection, and logs a warning, because a dashboard whose key no longer matches puts all its people under one limit.
  An IPv6 address is limited by its `/64`, the block one subscriber is usually given; an IPv4 address written as IPv6 is the IPv4 address.
- **The per-address limit drops from 60 to 20 a minute**, now that it is per person: enough for an office behind one address, far too few for credential stuffing from it.
  Callers other than the dashboard (a merchant's server) are limited by their connection, as before.

## Rejected alternatives

- **Forwarded headers trusted by network** (`ForwardedHeadersOptions.KnownNetworks` set to the dashboard's subnet): it works in compose, but in Azure the dashboard reaches the API over the internet from shared outbound addresses, and in compose the host's own connections to the API's published port arrive from the same network's gateway.
- **The dashboard reads the socket's address itself**, through a custom Node server around Next.js: no proxy to run in compose, but it gives up the standalone server Next.js builds, and Azure still puts a proxy in front, so the dashboard would need the header logic anyway.
- **No proxy in compose**, the dashboard trusting `X-Forwarded-For` as it comes: any browser could then name a new address with each attempt and never be limited by address.
- **Keeping one limit for the whole dashboard:** a single person could then spend everyone's sign-in budget.

## Consequences

- Compose has one more container, and the dashboard's container no longer publishes a port; `next start` or `npm run dev` without a proxy in front takes the socket's address, or a forged header, so a local dev server is not a place to test the limit.
- A change in the proxies in front of the dashboard is a change to `TRUSTED_PROXY_HOPS`; too high and every sign-in falls back to one limit for the dashboard, too low and the first proxy's address stands in for everyone behind it.
  Neither lets a browser choose its address.
- On Docker Desktop every browser on the host reaches Caddy from the same gateway address, so locally the limit is still one for the machine; a request from another address (another container on the compose network) gets its own, which the walk-through checked.
- The key is a second shared secret to rotate: set the new value in both places, the API's first, during which sign-ins from a dashboard with the old key fall back to the dashboard's limit and are logged.
