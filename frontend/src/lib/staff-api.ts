// The operator area's reads and moves, with the staff token from the dashboard's server (ADR 011).
import "server-only";
import { redirect } from "next/navigation";
import { apiOrigin } from "@/lib/api";
import { readStaffToken, staffArea } from "@/lib/staff-session";

export type DeadLetter = {
  id: number;
  merchantId: string;
  paymentId: string;
  eventType: string;
  attempts: number;
  lastStatusCode: number | null;
  lastError: string | null;
  endpointHost: string;
  createdAt: string;
};

/** A read the API could not answer; the operator area's `error.tsx` shows it and offers a retry. */
export class StaffApiUnavailableError extends Error {
  constructor(status?: number) {
    super(status ? `The staff API answered ${status}` : "The staff API did not answer");
    this.name = "StaffApiUnavailableError";
  }
}

const signIn = (returnTo: string, reason?: "expired"): never =>
  redirect(
    `${staffArea}/login?next=${encodeURIComponent(returnTo)}${reason ? `&reason=${reason}` : ""}`,
  );

/** The staff token, or a redirect to the staff sign-in that comes back to `returnTo`. */
async function requireStaffToken(returnTo: string): Promise<string> {
  return (await readStaffToken()) ?? signIn(returnTo);
}

/** Every merchant's dead-lettered webhook deliveries, newest first, the first page of them. */
export async function listDeadLetters(returnTo: string): Promise<DeadLetter[]> {
  const token = await requireStaffToken(returnTo);
  let response: Response;
  try {
    response = await fetch(`${apiOrigin}/api/v1/staff/webhook-deliveries?status=dead_lettered`, {
      headers: { authorization: `Bearer ${token}` },
      cache: "no-store",
      signal: AbortSignal.timeout(5_000),
    });
  } catch {
    throw new StaffApiUnavailableError();
  }

  if (response.status === 401 || response.status === 403) signIn(returnTo, "expired");
  const body: unknown = await response.json().catch(() => null);
  if (!response.ok || !isPage(body)) throw new StaffApiUnavailableError(response.status);
  return body.data.map((row) => ({
    id: row.id,
    merchantId: row.merchant_id,
    paymentId: row.payment_id,
    eventType: row.event_type,
    attempts: row.attempts,
    lastStatusCode: row.last_status_code,
    lastError: row.last_error,
    endpointHost: row.endpoint_host,
    createdAt: row.created_at,
  }));
}

export type ReplayResult =
  { kind: "replayed" } | { kind: "refused"; code: string } | { kind: "unavailable" };

/** Puts a dead-lettered delivery back on the queue; the worker sends it on its next run. */
export async function replayDelivery(id: number, returnTo: string): Promise<ReplayResult> {
  const token = await requireStaffToken(returnTo);
  let response: Response;
  try {
    response = await fetch(`${apiOrigin}/api/v1/staff/webhook-deliveries/${id}/replay`, {
      method: "POST",
      headers: { authorization: `Bearer ${token}` },
      cache: "no-store",
      signal: AbortSignal.timeout(5_000),
    });
  } catch {
    return { kind: "unavailable" };
  }

  if (response.status === 401 || response.status === 403) signIn(returnTo, "expired");
  if (response.ok) return { kind: "replayed" };
  const body: unknown = await response.json().catch(() => null);
  const code =
    typeof body === "object" && body !== null && "code" in body && typeof body.code === "string"
      ? body.code
      : undefined;
  return code ? { kind: "refused", code } : { kind: "unavailable" };
}

type ApiDeadLetter = {
  id: number;
  merchant_id: string;
  payment_id: string;
  event_type: string;
  attempts: number;
  last_status_code: number | null;
  last_error: string | null;
  endpoint_host: string;
  created_at: string;
};

function isPage(body: unknown): body is { data: ApiDeadLetter[] } {
  return typeof body === "object" && body !== null && "data" in body && Array.isArray(body.data);
}
