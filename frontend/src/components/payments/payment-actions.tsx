"use client";

import { useActionState } from "react";
import { moveAction } from "@/app/payments/[id]/actions";
import { buttonClasses } from "@/components/ui/button";
import type { PaymentStatus } from "@/lib/payment-status";

type Props = {
  paymentId: string;
  status: PaymentStatus;
  /** Made for this render of the page, so sending the same form again replays rather than repeats (ADR 007). */
  idempotencyKey: string;
  amount: string;
};

/**
 * The moves a merchant can make on a payment, as the lifecycle allows them: capture an authorized one, refund a
 * captured or settled one. One component holds both and the outcome, so when a capture succeeds and the page
 * re-renders with the refund in its place, the message about the capture stays.
 */
export function PaymentActions({ paymentId, status, idempotencyKey, amount }: Props) {
  const [state, formAction, pending] = useActionState(moveAction, {});
  const canCapture = status === "authorized";
  const canRefund = status === "captured" || status === "settled";

  const hidden = (operation: "capture" | "refund") => (
    <>
      <input type="hidden" name="id" value={paymentId} />
      <input type="hidden" name="operation" value={operation} />
      <input type="hidden" name="idempotency_key" value={idempotencyKey} />
    </>
  );

  // From sm the moves sit at the header's right edge, and stay there when the refund's confirmation opens.
  return (
    <div className="w-full sm:w-auto sm:max-w-md sm:text-right">
      {canCapture && (
        <form action={formAction}>
          {hidden("capture")}
          <button type="submit" disabled={pending} className={buttonClasses()}>
            {pending ? "Capturing…" : `Capture ${amount}`}
          </button>
        </form>
      )}
      {canRefund && (
        // A second step, so a refund is never one stray click away.
        <details className="group">
          <summary
            className={buttonClasses({
              variant: "secondary",
              className: "cursor-pointer list-none [&::-webkit-details-marker]:hidden",
            })}
          >
            Refund…
          </summary>
          <form
            action={formAction}
            className="mt-3 rounded-md border border-border bg-surface-2 p-4 text-left"
          >
            {hidden("refund")}
            <p className="text-sm text-pretty">
              Return the full {amount} to the cardholder. A refund cannot be undone.
            </p>
            <button
              type="submit"
              disabled={pending}
              className={buttonClasses({ className: "mt-3" })}
            >
              {pending ? "Refunding…" : `Refund ${amount}`}
            </button>
          </form>
        </details>
      )}
      {!canCapture && !canRefund && !state.message && (
        <p className="text-sm text-muted">No moves are open for a payment in this status.</p>
      )}
      {/* Both mounted from the start, so the outcome is announced when it arrives; empty, they take no space. */}
      <p role="status" className="mt-2 text-sm text-success empty:mt-0">
        {state.tone === "success" ? state.message : null}
      </p>
      <p role="alert" className="mt-2 text-sm text-danger empty:mt-0">
        {state.tone === "error" ? state.message : null}
      </p>
    </div>
  );
}
