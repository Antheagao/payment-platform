import Link from "next/link";
import { buttonClasses } from "@/components/ui/button";
import { currencies } from "@/lib/money";
import type { FilterField, PaymentFilters } from "@/lib/payment-filters";
import { paymentStatusDisplay, type PaymentStatus } from "@/lib/payment-status";

const statuses = Object.keys(paymentStatusDisplay) as PaymentStatus[];

const inputClasses =
  "mt-1.5 block min-h-10 w-full rounded-md border border-border-strong bg-surface px-3 text-sm text-fg aria-invalid:border-danger";

type Props = {
  filters: PaymentFilters;
  errors: Partial<Record<FilterField, string>>;
};

/**
 * The list's filters as a plain `GET` form: every view is a link, it works before the page's scripts load, and
 * the browser's back button steps through earlier searches.
 */
export function PaymentFiltersForm({ filters, errors }: Props) {
  const describedBy = (field: FilterField) => (errors[field] ? `${field}-error` : undefined);
  const invalid = (field: FilterField) => (errors[field] ? true : undefined);
  const message = (field: FilterField) =>
    errors[field] && (
      <p id={`${field}-error`} className="mt-1.5 text-sm text-danger">
        {errors[field]}
      </p>
    );

  const active = activeFilters(filters);
  return (
    // Folded away until it is needed, so at phone width the payments are on the first screen; open whenever a
    // filter is set or a field needs fixing.
    <details
      open={active > 0 || Object.keys(errors).length > 0}
      className="group rounded-lg border border-border bg-surface"
    >
      <summary className="flex min-h-12 cursor-pointer list-none items-center justify-between gap-3 rounded-lg px-4 font-medium sm:px-6 [&::-webkit-details-marker]:hidden">
        <span>
          Filters
          {active > 0 && (
            <span className="ml-2 text-sm font-normal text-muted">{active} active</span>
          )}
        </span>
        <svg
          aria-hidden="true"
          viewBox="0 0 20 20"
          className="size-4 text-muted transition-transform group-open:rotate-180"
          fill="currentColor"
        >
          <path d="M5.2 7.4a.75.75 0 0 1 1.06.04L10 11.4l3.74-3.96a.75.75 0 1 1 1.1 1.02l-4.3 4.54a.75.75 0 0 1-1.08 0l-4.3-4.54a.75.75 0 0 1 .04-1.06Z" />
        </svg>
      </summary>
      <form
        method="get"
        action="/payments"
        role="search"
        aria-label="Filter payments"
        className="border-t border-border p-4 sm:p-6"
      >
        <fieldset aria-describedby={describedBy("status")}>
          <legend className="text-sm font-medium">Status</legend>
          <div className="mt-2 flex flex-wrap gap-x-5 gap-y-2">
            {statuses.map((status) => (
              <label key={status} className="flex min-h-6 items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  name="status"
                  value={status}
                  defaultChecked={filters.statuses.includes(status)}
                  className="size-4 accent-primary"
                />
                {paymentStatusDisplay[status].label}
              </label>
            ))}
          </div>
          {message("status")}
        </fieldset>

        <div className="mt-5 grid gap-x-4 gap-y-5 sm:grid-cols-2 lg:grid-cols-3">
          <div>
            <label htmlFor="from" className="block text-sm font-medium">
              Created from
            </label>
            <input
              id="from"
              name="from"
              type="date"
              defaultValue={filters.from}
              aria-invalid={invalid("from")}
              aria-describedby={describedBy("from")}
              className={inputClasses}
            />
            {message("from")}
          </div>
          <div>
            <label htmlFor="to" className="block text-sm font-medium">
              Created to
            </label>
            <input
              id="to"
              name="to"
              type="date"
              defaultValue={filters.to}
              aria-invalid={invalid("to")}
              aria-describedby={describedBy("to")}
              className={inputClasses}
            />
            {message("to")}
          </div>
          <div>
            <label htmlFor="reference" className="block text-sm font-medium">
              Reference starts with
            </label>
            <input
              id="reference"
              name="reference"
              type="search"
              maxLength={100}
              defaultValue={filters.reference}
              aria-invalid={invalid("reference")}
              aria-describedby={describedBy("reference")}
              className={inputClasses}
            />
            {message("reference")}
          </div>
          <div>
            <label htmlFor="currency" className="block text-sm font-medium">
              Currency
            </label>
            <select
              id="currency"
              name="currency"
              defaultValue={filters.currency}
              aria-invalid={invalid("currency")}
              aria-describedby={describedBy("currency")}
              className={inputClasses}
            >
              <option value="">Any currency</option>
              {currencies.map((code) => (
                <option key={code} value={code}>
                  {code}
                </option>
              ))}
            </select>
            {message("currency")}
          </div>
          <div>
            <label htmlFor="min" className="block text-sm font-medium">
              Amount from
            </label>
            <input
              id="min"
              name="min"
              inputMode="decimal"
              autoComplete="off"
              placeholder="0.00"
              defaultValue={filters.min}
              aria-invalid={invalid("min")}
              aria-describedby={describedBy("min") ?? "amount-hint"}
              className={inputClasses}
            />
            {message("min")}
          </div>
          <div>
            <label htmlFor="max" className="block text-sm font-medium">
              Amount to
            </label>
            <input
              id="max"
              name="max"
              inputMode="decimal"
              autoComplete="off"
              placeholder="0.00"
              defaultValue={filters.max}
              aria-invalid={invalid("max")}
              aria-describedby={describedBy("max") ?? "amount-hint"}
              className={inputClasses}
            />
            {message("max")}
          </div>
        </div>
        <p id="amount-hint" className="mt-3 text-sm text-muted">
          Amounts are in the chosen currency, so filtering by amount needs one.
        </p>

        <div className="mt-5 flex flex-wrap gap-3">
          <button type="submit" className={buttonClasses()}>
            Apply filters
          </button>
          <Link href="/payments" className={buttonClasses({ variant: "secondary" })}>
            Clear
          </Link>
        </div>
      </form>
    </details>
  );
}

/** How many filters are set; a date or amount range counts once per end. */
function activeFilters(filters: PaymentFilters): number {
  return [
    filters.statuses.length > 0,
    filters.from,
    filters.to,
    filters.currency,
    filters.min,
    filters.max,
    filters.reference,
  ].filter(Boolean).length;
}
