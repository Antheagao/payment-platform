// Server-only access to the payment API. The browser never calls the API and never sees its origin or a
// token (ADR 004); every request goes through server code in this module.
import "server-only";

const apiOrigin = process.env.API_ORIGIN ?? "http://localhost:5080";

/** What the API's readiness probe says: serving, up but not ready, or not answering at all. */
export type ApiHealth = "ready" | "not_ready" | "unreachable";

/**
 * Asks the API's readiness probe. Never throws: the status badge must render whatever the answer,
 * and a slow API is cut off after five seconds rather than holding the page. The probe answers
 * within three, even with its database down, so an outage reads as not ready, never unreachable.
 */
export async function getApiHealth(): Promise<ApiHealth> {
  try {
    const response = await fetch(`${apiOrigin}/health/ready`, {
      cache: "no-store",
      signal: AbortSignal.timeout(5_000),
    });
    return response.ok ? "ready" : "not_ready";
  } catch {
    return "unreachable";
  }
}
