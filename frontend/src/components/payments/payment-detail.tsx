import { notFound } from "next/navigation";
import { PaymentActions } from "@/components/payments/payment-actions";
import { PaymentTimeline } from "@/components/payments/payment-timeline";
import { Badge } from "@/components/ui/badge";
import { classNames } from "@/lib/class-names";
import { formatMoney } from "@/lib/money";
import { brandName, describeReason, formatInstant } from "@/lib/payment-display";
import { paymentStatusDisplay } from "@/lib/payment-status";
import { getPayment } from "@/lib/payments-api";

/** One payment, read for this request: its header and moves, its details, and its history. */
export async function PaymentDetail({ id }: { id: string }) {
  const payment = await getPayment(id, `/payments/${id}`);
  if (!payment) notFound();

  const status = paymentStatusDisplay[payment.status];
  const amount = formatMoney(payment.amountMinor, payment.currency);
  const details: [string, string, string?][] = [
    ["Payment id", payment.id, "font-mono text-xs"],
    [
      "Card",
      `${brandName(payment.card.brand)} •••• ${payment.card.last4}, expires ${String(payment.card.expMonth).padStart(2, "0")}/${payment.card.expYear}`,
    ],
    ["Created", formatInstant(payment.createdAt)],
    ["Last change", formatInstant(payment.updatedAt)],
  ];
  if (payment.authorizationReference)
    details.push(["Authorization reference", payment.authorizationReference, "font-mono text-xs"]);
  if (payment.failureReason)
    details.push(["Failure reason", describeReason(payment.failureReason)]);

  return (
    <>
      <div className="mt-4 flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0">
          <h1 className="text-2xl font-semibold tracking-tight break-words sm:text-3xl">
            {payment.merchantReference ?? "Payment without a reference"}
          </h1>
          <p className="mt-2 flex flex-wrap items-center gap-3">
            <span className="text-3xl font-semibold tabular-nums">{amount}</span>
            <Badge tone={status.tone}>{status.label}</Badge>
          </p>
          <p className="mt-1 text-sm text-muted">{status.meaning}</p>
        </div>
        <PaymentActions
          paymentId={payment.id}
          status={payment.status}
          idempotencyKey={crypto.randomUUID()}
          amount={amount}
        />
      </div>

      <div className="mt-10 grid gap-10 lg:grid-cols-5">
        <section aria-labelledby="details-heading" className="min-w-0 lg:col-span-2">
          <h2 id="details-heading" className="text-base font-semibold">
            Details
          </h2>
          {/* Side by side where the panel is wide; stacked in the narrow column from lg, and at phone width,
              so a 36-character id keeps to one line. */}
          <dl className="mt-4 divide-y divide-border rounded-lg border border-border bg-surface">
            {details.map(([term, value, valueClass]) => (
              <div
                key={term}
                className="grid gap-1 px-3 py-3 sm:grid-cols-3 sm:gap-4 sm:px-4 lg:grid-cols-1 lg:gap-1"
              >
                <dt className="text-sm text-muted">{term}</dt>
                <dd
                  className={classNames(
                    "min-w-0 text-sm break-all sm:col-span-2 lg:col-span-1",
                    valueClass,
                  )}
                >
                  {value}
                </dd>
              </div>
            ))}
          </dl>
        </section>

        <section aria-labelledby="history-heading" className="min-w-0 lg:col-span-3">
          <h2 id="history-heading" className="text-base font-semibold">
            History
          </h2>
          <p className="mt-1 text-sm text-muted">Every change, oldest first, with who made it.</p>
          <PaymentTimeline history={payment.history} />
        </section>
      </div>
    </>
  );
}

/** The page's shape while the payment is read, so nothing moves when it arrives. */
export function PaymentDetailFallback() {
  return (
    <div aria-hidden="true">
      <div className="mt-4 h-8 w-48 rounded-md bg-surface-2 sm:h-9" />
      <div className="mt-2 h-9 w-40 rounded-md bg-surface-2" />
      <div className="mt-2 h-5 w-64 max-w-full rounded-md bg-surface-2" />
      <div className="mt-10 grid gap-10 lg:grid-cols-5">
        <div className="h-72 rounded-lg bg-surface-2 lg:col-span-2" />
        <div className="h-72 rounded-lg bg-surface-2 lg:col-span-3" />
      </div>
    </div>
  );
}
