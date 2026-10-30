"use client";

import { useActionState } from "react";
import { replayAction } from "@/app/operator/actions";
import { buttonClasses } from "@/components/ui/button";

export type DeadLetterRow = {
  id: number;
  merchantId: string;
  paymentId: string;
  eventType: string;
  attempts: number;
  failure: string;
  endpointHost: string;
  createdAt: string;
  createdAtLabel: string;
};

/**
 * The dead letters, each with its replay. One action state for the whole list, so the outcome stays on screen
 * after the refresh that takes the replayed row away.
 */
export function DeadLetterList({ rows }: { rows: readonly DeadLetterRow[] }) {
  const [state, formAction, pending] = useActionState(replayAction, {});

  return (
    <div>
      {/* Mounted from the start, so the outcome is announced when it arrives; empty, they take no space. */}
      <p role="status" className="mb-4 text-sm text-success empty:mb-0">
        {state.tone === "success" ? state.message : null}
      </p>
      <p role="alert" className="mb-4 text-sm text-danger empty:mb-0">
        {state.tone === "error" ? state.message : null}
      </p>

      {rows.length === 0 ? (
        <p className="rounded-lg border border-border bg-surface p-6 text-sm text-pretty text-muted">
          No dead-lettered deliveries. Every webhook was delivered, or is still being retried.
        </p>
      ) : (
        <ul className="space-y-3">
          {rows.map((row) => (
            <li key={row.id} className="rounded-lg border border-border bg-surface p-4">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <p className="font-medium">
                    <span className="font-mono">{row.eventType}</span> to {row.endpointHost}
                  </p>
                  <p className="mt-1 text-sm text-muted">
                    {row.attempts} attempts, last: {row.failure} ·{" "}
                    <time dateTime={row.createdAt} className="whitespace-nowrap">
                      {row.createdAtLabel}
                    </time>
                  </p>
                </div>
                <form action={formAction}>
                  <input type="hidden" name="id" value={row.id} />
                  <button
                    type="submit"
                    disabled={pending}
                    className={buttonClasses({ variant: "secondary", size: "sm" })}
                  >
                    Replay
                  </button>
                </form>
              </div>
              <dl className="mt-3 flex flex-wrap gap-x-6 gap-y-1 text-xs">
                {[
                  ["Delivery", String(row.id)],
                  ["Merchant", row.merchantId],
                  ["Payment", row.paymentId],
                ].map(([term, value]) => (
                  // Stacked at phone width, so a 36-character id keeps to one line.
                  <div key={term} className="min-w-0 sm:flex sm:gap-2">
                    <dt className="text-muted">{term}</dt>
                    <dd className="font-mono break-all">{value}</dd>
                  </div>
                ))}
              </dl>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
