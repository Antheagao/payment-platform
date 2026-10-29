"use server";

import { refresh } from "next/cache";
import { movePayment, type MoveOperation } from "@/lib/payments-api";

export type MoveState = { tone?: "success" | "error"; message?: string };

const paymentId = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

const done: Record<MoveOperation, string> = {
  capture: "Captured. The funds are claimed and settle with the next batch.",
  refund: "Refunded. The money is on its way back to the cardholder.",
};

/** What the API's refusal means for the person who asked, by its code (ADR 006). */
function refusal(operation: MoveOperation, code: string, retryAfterSeconds?: number): string {
  switch (code) {
    case "payment_not_capturable":
      return "This payment can no longer be captured: it is not authorized any more.";
    case "payment_not_refundable":
      return "This payment can no longer be refunded: it has not been captured, or it was refunded already.";
    case `${operation}_declined`:
      return `The card network declined the ${operation}. Nothing changed.`;
    case "acquirer_unavailable":
      return `The card network did not answer. Nothing changed; try again${retryAfterSeconds ? ` in ${retryAfterSeconds} seconds` : ""}.`;
    case "idempotency_request_in_progress":
      return `This ${operation} is still being processed. Wait a moment, then reload the page.`;
    case "not_found":
      return "This payment is not one of yours any more.";
    default:
      return `The payments service refused the ${operation} (${code}).`;
  }
}

/**
 * Captures or refunds the payment from its page. A Server Action is a public endpoint, so it checks what it is
 * sent; the API checks again, under the form's own `Idempotency-Key`, so a form sent twice moves money once.
 */
export async function moveAction(_previous: MoveState, form: FormData): Promise<MoveState> {
  const id = String(form.get("id") ?? "");
  const operation = form.get("operation");
  const key = String(form.get("idempotency_key") ?? "");
  if (!paymentId.test(id) || (operation !== "capture" && operation !== "refund") || !key) {
    return {
      tone: "error",
      message: "That request was incomplete. Reload the page and try again.",
    };
  }

  const result = await movePayment(id, operation, key, `/payments/${id}`);
  switch (result.kind) {
    case "moved":
      refresh();
      return { tone: "success", message: done[operation] };
    case "refused":
      // The payment moved on elsewhere, in another tab or by the settlement job: show where it is now.
      if (result.code === "payment_not_capturable" || result.code === "payment_not_refundable") {
        refresh();
      }
      return { tone: "error", message: refusal(operation, result.code, result.retryAfterSeconds) };
    case "unavailable":
      return {
        tone: "error",
        message: `The payments service did not answer. Nothing changed; try the ${operation} again.`,
      };
  }
}
