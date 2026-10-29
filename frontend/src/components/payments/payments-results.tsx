import Link from "next/link";
import { Badge } from "@/components/ui/badge";
import { buttonClasses } from "@/components/ui/button";
import { formatMoney } from "@/lib/money";
import { pageHref, type ParsedFilters } from "@/lib/payment-filters";
import { paymentStatusDisplay } from "@/lib/payment-status";
import { listPayments, type CardBrand, type PaymentSummary } from "@/lib/payments-api";

const brandNames: Record<CardBrand, string> = {
  visa: "Visa",
  mastercard: "Mastercard",
  amex: "Amex",
  discover: "Discover",
};

// UTC, written out, so a time reads the same on every machine and matches the API's.
const timeFormat = new Intl.DateTimeFormat("en-US", {
  dateStyle: "medium",
  timeStyle: "short",
  timeZone: "UTC",
});

function created(payment: PaymentSummary): string {
  return `${timeFormat.format(new Date(payment.createdAt))} UTC`;
}

function card(payment: PaymentSummary): string {
  return `${brandNames[payment.card.brand] ?? payment.card.brand} •••• ${payment.card.last4}`;
}

type Props = {
  parsed: ParsedFilters;
  cursor: string | undefined;
  returnTo: string;
};

/** One page of the merchant's payments for the filters, or the reason there is none to show. */
export async function PaymentsResults({ parsed, cursor, returnTo }: Props) {
  if (!parsed.query) {
    return <Notice title="Fix the filters marked above to see payments." />;
  }

  const query = new URLSearchParams(parsed.query);
  if (cursor) query.set("cursor", cursor);
  const page = await listPayments(query, returnTo);
  if (page.kind === "invalid") {
    return (
      <Notice
        title="That page of payments is no longer available."
        action={{ href: "/payments", label: "Start from the newest" }}
      />
    );
  }

  if (page.payments.length === 0) {
    return parsed.filtered ? (
      <Notice
        title="No payments match these filters."
        detail="Widen the dates or the amounts, or choose more statuses."
        action={{ href: "/payments", label: "Clear filters" }}
      />
    ) : (
      <Notice
        title="No payments yet."
        detail="Payments you take through the API appear here, newest first."
      />
    );
  }

  return (
    <section aria-labelledby="results-heading">
      <h2 id="results-heading" className="sr-only">
        Payments, newest first
      </h2>

      {/* A table where it fits, a list of cards where it does not: no sideways scrolling at phone width. */}
      <div className="hidden rounded-lg border border-border bg-surface px-6 lg:block">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-border text-left text-muted">
              <th scope="col" className="py-3 pr-4 font-medium">
                Created
              </th>
              <th scope="col" className="py-3 pr-4 font-medium">
                Reference
              </th>
              <th scope="col" className="py-3 pr-4 font-medium">
                Card
              </th>
              <th scope="col" className="py-3 pr-4 font-medium">
                Status
              </th>
              <th scope="col" className="py-3 text-right font-medium">
                Amount
              </th>
            </tr>
          </thead>
          <tbody>
            {page.payments.map((payment) => (
              <tr key={payment.id} className="border-b border-border last:border-0">
                <td className="py-3 pr-4 whitespace-nowrap text-muted">{created(payment)}</td>
                <td className="max-w-56 truncate py-3 pr-4 font-medium">
                  {payment.merchantReference ?? "No reference"}
                </td>
                <td className="py-3 pr-4 whitespace-nowrap">{card(payment)}</td>
                <td className="py-3 pr-4">
                  <Badge tone={paymentStatusDisplay[payment.status].tone}>
                    {paymentStatusDisplay[payment.status].label}
                  </Badge>
                </td>
                <td className="py-3 text-right font-medium whitespace-nowrap tabular-nums">
                  {formatMoney(payment.amountMinor, payment.currency)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <ul className="space-y-3 lg:hidden">
        {page.payments.map((payment) => (
          <li key={payment.id} className="rounded-lg border border-border bg-surface p-4">
            <div className="flex items-start justify-between gap-3">
              <p className="min-w-0 truncate font-medium">
                {payment.merchantReference ?? "No reference"}
              </p>
              <Badge tone={paymentStatusDisplay[payment.status].tone}>
                {paymentStatusDisplay[payment.status].label}
              </Badge>
            </div>
            <p className="mt-2 text-lg font-semibold tabular-nums">
              {formatMoney(payment.amountMinor, payment.currency)}
            </p>
            <p className="mt-1 text-sm text-muted">
              {card(payment)} · {created(payment)}
            </p>
          </li>
        ))}
      </ul>

      <nav aria-label="Pages" className="mt-6 flex flex-wrap justify-between gap-3">
        {cursor ? (
          <Link href={pageHref(parsed.filters)} className={buttonClasses({ variant: "secondary" })}>
            Newest payments
          </Link>
        ) : (
          <span />
        )}
        {page.nextCursor && (
          <Link
            href={pageHref(parsed.filters, page.nextCursor)}
            className={buttonClasses({ variant: "secondary" })}
          >
            Older payments
          </Link>
        )}
      </nav>
    </section>
  );
}

function Notice({
  title,
  detail,
  action,
}: {
  title: string;
  detail?: string;
  action?: { href: string; label: string };
}) {
  return (
    <div className="rounded-lg border border-dashed border-border-strong px-6 py-10 text-center">
      <p className="font-medium text-balance">{title}</p>
      {detail && <p className="mt-1 text-sm text-pretty text-muted">{detail}</p>}
      {action && (
        <Link
          href={action.href}
          className={buttonClasses({ variant: "secondary", className: "mt-4" })}
        >
          {action.label}
        </Link>
      )}
    </div>
  );
}

/** The results' shape while they load, so the page does not jump when they arrive. */
export function PaymentsResultsFallback() {
  return (
    <div aria-hidden="true" className="space-y-3">
      {Array.from({ length: 6 }, (_, index) => (
        <div key={index} className="h-14 rounded-lg bg-surface-2" />
      ))}
    </div>
  );
}
