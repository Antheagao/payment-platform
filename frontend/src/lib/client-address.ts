// The person's address, as the proxies in front of the dashboard saw it (ADR 010).
import "server-only";
import { isIP } from "node:net";

/**
 * The browser's address from the request's `X-Forwarded-For`. Each proxy appends the address that connected to
 * it, so with `hops` trusted proxies in front of the dashboard the person's address is that many entries from the
 * right: one for Caddy in compose or App Service's front end in Azure (which may add a port), two with Front Door
 * ahead of App Service (`TRUSTED_PROXY_HOPS`). Entries further left came from the browser and prove nothing.
 * Undefined when the header is short of the hops, or the entry is not an address.
 */
export function clientAddress(
  forwardedFor: string | null,
  hops = Number(process.env.TRUSTED_PROXY_HOPS ?? 1),
): string | undefined {
  const entries = forwardedFor?.split(",") ?? [];
  if (!Number.isInteger(hops) || hops < 1 || entries.length < hops) return undefined;
  const entry = entries[entries.length - hops].trim();
  const address =
    entry.match(/^\[(.+)\](?::\d+)?$/)?.[1] ?? entry.match(/^([\d.]+):\d+$/)?.[1] ?? entry;
  return isIP(address) ? address : undefined;
}
