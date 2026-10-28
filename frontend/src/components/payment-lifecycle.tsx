import { Badge } from "@/components/ui/badge";
import { lifecycleOrder, paymentStatusDisplay, type PaymentStatus } from "@/lib/payment-status";

const branches: readonly PaymentStatus[] = ["failed", "refunded"];

/** Every payment status with what it means: the legend the payments list and detail pages use. */
export function PaymentLifecycle() {
  return (
    <section
      aria-labelledby="lifecycle-heading"
      className="rounded-lg border border-border bg-surface p-6"
    >
      <h2 id="lifecycle-heading" className="text-base font-semibold">
        Payment lifecycle
      </h2>
      <p className="mt-1 text-sm text-pretty text-muted">
        A payment moves left to right; a decline ends it as failed, and a refund can follow capture
        or settlement.
      </p>
      <ol className="mt-5 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        {lifecycleOrder.map((status, index) => (
          <StatusRow key={status} status={status} step={index + 1} />
        ))}
      </ol>
      <ul className="mt-3 grid gap-3 border-t border-border pt-3 sm:grid-cols-2 lg:grid-cols-4">
        {branches.map((status) => (
          <StatusRow key={status} status={status} />
        ))}
      </ul>
    </section>
  );
}

function StatusRow({ status, step }: { status: PaymentStatus; step?: number }) {
  const { label, tone, meaning } = paymentStatusDisplay[status];
  return (
    <li className="rounded-md bg-surface-2 p-3">
      <div className="flex items-center gap-2">
        {step !== undefined && (
          <span className="text-xs font-medium text-muted tabular-nums">{step}.</span>
        )}
        <Badge tone={tone}>{label}</Badge>
      </div>
      <p className="mt-2 text-sm text-pretty text-muted">{meaning}</p>
    </li>
  );
}
