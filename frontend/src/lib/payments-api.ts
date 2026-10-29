// The merchant's payments, read with the session's token from the dashboard's server (ADR 004).
import "server-only";
import { redirect } from "next/navigation";
import { apiOrigin } from "@/lib/api";
import type { PaymentStatus } from "@/lib/payment-status";
import { readToken } from "@/lib/session";

export type CardBrand = "visa" | "mastercard" | "amex" | "discover";

export type PaymentSummary = {
  id: string;
  status: PaymentStatus;
  amountMinor: number;
  currency: string;
  merchantReference: string | null;
  card: { brand: CardBrand; last4: string };
  createdAt: string;
  updatedAt: string;
};

export type PaymentPage =
  | { kind: "page"; payments: PaymentSummary[]; nextCursor: string | null }
  | { kind: "invalid"; fields: Readonly<Record<string, readonly string[]>> };

/** A data read the API could not answer; the segment's `error.tsx` shows it and offers a retry. */
export class PaymentsUnavailableError extends Error {
  constructor(status?: number) {
    super(status ? `The payments API answered ${status}` : "The payments API did not answer");
    this.name = "PaymentsUnavailableError";
  }
}

/**
 * The session's token, or a redirect to sign in that comes back to `returnTo`. The proxy already sent a browser
 * with no cookie away; this is the check for a page that reads data, which the cookie alone does not prove.
 */
export async function requireToken(returnTo: string): Promise<string> {
  const token = await readToken();
  if (!token) redirect(`/login?next=${encodeURIComponent(returnTo)}`);
  return token;
}

function signInAgain(returnTo: string): never {
  redirect(`/login?next=${encodeURIComponent(returnTo)}&reason=expired`);
}

/** One page of the merchant's payments, newest first; `query` is the API's own query string. */
export async function listPayments(query: URLSearchParams, returnTo: string): Promise<PaymentPage> {
  const token = await requireToken(returnTo);
  let response: Response;
  try {
    response = await fetch(`${apiOrigin}/api/v1/payments?${query}`, {
      headers: { authorization: `Bearer ${token}` },
      cache: "no-store",
      signal: AbortSignal.timeout(5_000),
    });
  } catch {
    throw new PaymentsUnavailableError();
  }

  if (response.status === 401) signInAgain(returnTo);
  const body: unknown = await response.json().catch(() => null);
  if (response.status === 400) return { kind: "invalid", fields: fieldErrors(body) };
  if (!response.ok || !isPage(body)) throw new PaymentsUnavailableError(response.status);

  return {
    kind: "page",
    payments: body.data.map((payment) => ({
      id: payment.id,
      status: payment.status,
      amountMinor: payment.amount_minor,
      currency: payment.currency,
      merchantReference: payment.merchant_reference,
      card: payment.card,
      createdAt: payment.created_at,
      updatedAt: payment.updated_at,
    })),
    nextCursor: body.next_cursor,
  };
}

type ApiSummary = {
  id: string;
  status: PaymentStatus;
  amount_minor: number;
  currency: string;
  merchant_reference: string | null;
  card: { brand: CardBrand; last4: string };
  created_at: string;
  updated_at: string;
};

function isPage(body: unknown): body is { data: ApiSummary[]; next_cursor: string | null } {
  return (
    typeof body === "object" &&
    body !== null &&
    "data" in body &&
    Array.isArray(body.data) &&
    "next_cursor" in body &&
    (body.next_cursor === null || typeof body.next_cursor === "string")
  );
}

function fieldErrors(body: unknown): Record<string, string[]> {
  if (typeof body !== "object" || body === null || !("errors" in body)) return {};
  const errors = body.errors;
  if (typeof errors !== "object" || errors === null) return {};
  return Object.fromEntries(
    Object.entries(errors).map(([field, codes]) => [
      field,
      Array.isArray(codes) ? codes.filter((code) => typeof code === "string") : [],
    ]),
  );
}
