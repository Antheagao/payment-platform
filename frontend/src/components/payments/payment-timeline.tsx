import { Badge } from "@/components/ui/badge";
import { describeChange, formatInstant } from "@/lib/payment-display";
import { paymentStatusDisplay } from "@/lib/payment-status";
import type { PaymentHistoryEntry } from "@/lib/payments-api";

/**
 * A payment's audit trail (R24) as a timeline, oldest first: each change, who made it, when, and its correlation
 * id, so a question about one change can be followed into the logs and the trace.
 */
export function PaymentTimeline({ history }: { history: readonly PaymentHistoryEntry[] }) {
  return (
    <ol className="mt-4">
      {history.map((entry, index) => {
        const change = describeChange(entry);
        const last = index === history.length - 1;
        return (
          <li key={`${entry.occurredAt}-${index}`} className="relative flex gap-4 pb-6 last:pb-0">
            {/* The line between steps; decorative, the order is the list's. */}
            {!last && (
              <span
                aria-hidden="true"
                className="absolute top-6 bottom-0 left-1.5 w-px bg-border"
              />
            )}
            <span
              aria-hidden="true"
              className="relative mt-1.5 size-3 shrink-0 rounded-full border-2 border-border-strong bg-surface"
            />
            <div className="min-w-0">
              <p className="flex flex-wrap items-center gap-2">
                <span className="font-medium">{change.title}</span>
                <Badge tone={paymentStatusDisplay[entry.to].tone}>
                  {paymentStatusDisplay[entry.to].label}
                </Badge>
              </p>
              <p className="mt-1 text-sm text-muted">
                {change.by} ·{" "}
                <time dateTime={entry.occurredAt} className="whitespace-nowrap">
                  {formatInstant(entry.occurredAt)}
                </time>
              </p>
              {change.reason && <p className="mt-1 text-sm">{change.reason}</p>}
              {entry.correlationId && (
                <p className="mt-1 text-xs text-muted">
                  Correlation id{" "}
                  <code className="block font-mono break-all text-fg sm:inline">
                    {entry.correlationId}
                  </code>
                </p>
              )}
            </div>
          </li>
        );
      })}
    </ol>
  );
}
