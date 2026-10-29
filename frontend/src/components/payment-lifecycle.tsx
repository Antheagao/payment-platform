import { Badge } from "@/components/ui/badge";
import { classNames } from "@/lib/class-names";
import { lifecycleOrder, paymentStatusDisplay, type PaymentStatus } from "@/lib/payment-status";

// Where each terminal branch leaves the happy path, matching `PaymentLifecycle` in the domain.
const branches: readonly { status: PaymentStatus; from: string; placement: string }[] = [
  { status: "failed", from: "From pending", placement: "lg:col-start-1" },
  {
    status: "refunded",
    from: "From captured or settled",
    placement: "lg:col-span-2 lg:col-start-3",
  },
];

/** Every payment status with what it means: the legend the payments list and detail pages use. */
export function PaymentLifecycle() {
  return (
    <section aria-labelledby="lifecycle-heading">
      <h2 id="lifecycle-heading" className="text-base font-semibold">
        Payment lifecycle
      </h2>
      <p className="mt-1 text-sm text-pretty text-muted">
        Each payment takes these steps in order; a decline ends it as failed, and a refund can
        follow capture or settlement.
      </p>
      <div className="mt-4 rounded-lg border border-border bg-surface p-4 sm:p-6">
        <ol className="grid gap-6 lg:grid-cols-4">
          {lifecycleOrder.map((status, index) => (
            <li key={status} className="relative rounded-md bg-surface-2 p-4">
              <p className="text-xs font-medium text-muted tabular-nums">Step {index + 1}</p>
              <StatusMeaning status={status} />
              {index < lifecycleOrder.length - 1 && <NextArrow />}
            </li>
          ))}
        </ol>
        <ul className="mt-6 grid gap-4 border-t border-border pt-6 lg:grid-cols-4">
          {branches.map(({ status, from, placement }) => (
            <li
              key={status}
              className={classNames("rounded-md border border-dashed border-border p-4", placement)}
            >
              <p className="flex items-center gap-1.5 text-xs font-medium text-muted">
                <BranchIcon />
                {from}
              </p>
              <StatusMeaning status={status} />
            </li>
          ))}
        </ul>
      </div>
    </section>
  );
}

function StatusMeaning({ status }: { status: PaymentStatus }) {
  const { label, tone, meaning } = paymentStatusDisplay[status];
  return (
    <>
      <Badge tone={tone} className="mt-2">
        {label}
      </Badge>
      <p className="mt-2 text-sm text-pretty text-muted">{meaning}</p>
    </>
  );
}

/** Points to the next step: down the gap when the steps stack, right when they sit in a row. */
function NextArrow() {
  return (
    <svg
      aria-hidden="true"
      viewBox="0 0 16 16"
      className="absolute -bottom-5 left-1/2 size-4 -translate-x-1/2 rotate-90 text-border-strong lg:top-1/2 lg:-right-5 lg:bottom-auto lg:left-auto lg:translate-x-0 lg:-translate-y-1/2 lg:rotate-0"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.75"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <path d="M6 3.5 10.5 8 6 12.5" />
    </svg>
  );
}

function BranchIcon() {
  return (
    <svg
      aria-hidden="true"
      viewBox="0 0 16 16"
      className="size-3.5"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.75"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <path d="M4 2.5v5a2 2 0 0 0 2 2h6.5M10 7l2.5 2.5L10 12" />
    </svg>
  );
}
