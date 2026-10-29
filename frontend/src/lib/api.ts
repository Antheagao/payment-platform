// Server-only access to the payment API and the worker. The browser never calls either and never sees
// their origins or a token (ADR 004); every request goes through server code in this module.
import "server-only";
import { connection } from "next/server";
import { cache } from "react";

const apiOrigin = process.env.API_ORIGIN ?? "http://localhost:5080";
const workerOrigin = process.env.WORKER_ORIGIN ?? "http://localhost:5081";

/** What a readiness probe says: serving, up but not ready, or not answering at all. */
export type Readiness = "ready" | "not_ready" | "unreachable";

/** One named check in a readiness answer, as the services' health writer spells it. */
export type CheckStatus = "healthy" | "degraded" | "unhealthy";

export type ServiceHealth = {
  readiness: Readiness;
  /** From the dashboard's server to the answer, in milliseconds; absent when nothing answered. */
  roundTripMs?: number;
  /** How long the service took to run its own checks, as it reported it. */
  checksMs?: number;
  /** The service's checks by name (`database`, `self`); empty when nothing answered or the body was unreadable. */
  checks: Readonly<Record<string, CheckStatus>>;
};

const checkStatuses: readonly string[] = [
  "healthy",
  "degraded",
  "unhealthy",
] satisfies CheckStatus[];

/**
 * Asks a readiness probe. Never throws: the status display must render whatever the answer, and a
 * slow service is cut off after five seconds rather than holding the page. The probes answer within
 * three, even with their database down, so an outage reads as not ready, never unreachable.
 */
async function probe(origin: string): Promise<ServiceHealth> {
  // The round trip is timed per request, so nothing below may run while the page is prerendered.
  await connection();
  const started = performance.now();
  try {
    const response = await fetch(`${origin}/health/ready`, {
      cache: "no-store",
      signal: AbortSignal.timeout(5_000),
    });
    const roundTripMs = performance.now() - started;
    const report = await readReport(response);
    return { readiness: response.ok ? "ready" : "not_ready", roundTripMs, ...report };
  } catch {
    return { readiness: "unreachable", checks: {} };
  }
}

/** The health writer's body (`{"status", "duration_ms", "checks"}`), keeping only what parses. */
async function readReport(response: Response): Promise<Pick<ServiceHealth, "checksMs" | "checks">> {
  const body: unknown = await response.json().catch(() => null);
  if (typeof body !== "object" || body === null) return { checks: {} };

  const checks: Record<string, CheckStatus> = {};
  if ("checks" in body && typeof body.checks === "object" && body.checks !== null) {
    for (const [name, status] of Object.entries(body.checks)) {
      if (typeof status === "string" && checkStatuses.includes(status)) {
        checks[name] = status as CheckStatus;
      }
    }
  }
  const checksMs =
    "duration_ms" in body && typeof body.duration_ms === "number" ? body.duration_ms : undefined;
  return { checksMs, checks };
}

/** The API's readiness, asked once per request however many components show it. */
export const getApiHealth = cache(() => probe(apiOrigin));

/** The worker's readiness, asked once per request. */
export const getWorkerHealth = cache(() => probe(workerOrigin));
