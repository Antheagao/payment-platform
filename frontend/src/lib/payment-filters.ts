import { isCurrency, toMinorUnits, type CurrencyCode } from "@/lib/money";
import { paymentStatusDisplay, type PaymentStatus } from "@/lib/payment-status";

/** The list's filters as the page's URL holds them, which is also what the form shows. */
export type PaymentFilters = {
  statuses: PaymentStatus[];
  /** Inclusive dates, `YYYY-MM-DD`, read as UTC days. */
  from: string;
  to: string;
  currency: CurrencyCode | "";
  /** In the currency's major unit, as typed. */
  min: string;
  max: string;
  reference: string;
};

export type FilterField = "status" | "from" | "to" | "currency" | "min" | "max" | "reference";

export type ParsedFilters = {
  filters: PaymentFilters;
  /** The API's query (`GET /api/v1/payments`), without the cursor; null while any field is wrong. */
  query: URLSearchParams | null;
  errors: Partial<Record<FilterField, string>>;
  /** Whether any filter is set, to tell "no payments yet" from "none match". */
  filtered: boolean;
};

type SearchParams = Record<string, string | string[] | undefined>;

const statuses = Object.keys(paymentStatusDisplay) as PaymentStatus[];
const datePattern = /^\d{4}-\d{2}-\d{2}$/;
const referenceMaxLength = 100;

function all(value: string | string[] | undefined): string[] {
  return value === undefined ? [] : Array.isArray(value) ? value : [value];
}

function one(value: string | string[] | undefined): string {
  return (all(value)[0] ?? "").trim();
}

function isDate(text: string): boolean {
  if (!datePattern.test(text)) return false;
  const day = new Date(`${text}T00:00:00Z`);
  return !Number.isNaN(day.getTime()) && day.toISOString().startsWith(text);
}

/** The day after `date`, because the API's `created_to` is exclusive and the form's "to" includes its day. */
function dayAfter(date: string): string {
  const day = new Date(`${date}T00:00:00Z`);
  day.setUTCDate(day.getUTCDate() + 1);
  return day.toISOString().slice(0, 10);
}

/**
 * Reads the list's filters from the page's query string, checks them as the API would, and builds the API's
 * query. Every problem is a field message, so the form can show it beside the field; the API still checks
 * everything again.
 */
export function parseFilters(params: SearchParams): ParsedFilters {
  const errors: ParsedFilters["errors"] = {};
  const chosen = all(params.status).flatMap((value) => value.split(","));
  const known = chosen.filter((value): value is PaymentStatus =>
    (statuses as string[]).includes(value),
  );
  if (known.length !== chosen.length) errors.status = "Choose statuses from the list.";

  const from = one(params.from);
  const to = one(params.to);
  if (from && !isDate(from)) errors.from = "Enter a date as YYYY-MM-DD.";
  if (to && !isDate(to)) errors.to = "Enter a date as YYYY-MM-DD.";
  if (from && to && isDate(from) && isDate(to) && from > to)
    errors.to = "The end date comes before the start date.";

  const currencyText = one(params.currency);
  const currency = isCurrency(currencyText) ? currencyText : "";
  if (currencyText && !currency) errors.currency = "Choose a currency from the list.";

  const min = one(params.min);
  const max = one(params.max);
  let minMinor: number | null = null;
  let maxMinor: number | null = null;
  if ((min || max) && !currency) {
    errors[min ? "min" : "max"] = "Choose a currency to filter by amount.";
  } else if (currency) {
    if (min && (minMinor = toMinorUnits(min, currency)) === null)
      errors.min = `Enter an amount in ${currency}.`;
    if (max && (maxMinor = toMinorUnits(max, currency)) === null)
      errors.max = `Enter an amount in ${currency}.`;
    if (minMinor !== null && maxMinor !== null && minMinor > maxMinor)
      errors.max = "The highest amount is below the lowest.";
  }

  const reference = one(params.reference);
  if (reference.length > referenceMaxLength)
    errors.reference = `A reference is at most ${referenceMaxLength} characters.`;

  const filters: PaymentFilters = {
    statuses: [...new Set(known)],
    from,
    to,
    currency,
    min,
    max,
    reference,
  };
  const filtered = Boolean(
    filters.statuses.length || from || to || currency || min || max || reference,
  );
  if (Object.keys(errors).length > 0) return { filters, query: null, errors, filtered };

  const query = new URLSearchParams();
  if (filters.statuses.length) query.set("status", filters.statuses.join(","));
  if (from) query.set("created_from", from);
  if (to) query.set("created_to", dayAfter(to));
  if (currency) query.set("currency", currency);
  if (minMinor !== null) query.set("amount_min_minor", String(minMinor));
  if (maxMinor !== null) query.set("amount_max_minor", String(maxMinor));
  if (reference) query.set("reference", reference);
  return { filters, query, errors, filtered };
}

/** The page's own query string for these filters and a cursor, so "Older payments" keeps every filter. */
export function pageHref(filters: PaymentFilters, cursor?: string): string {
  const params = new URLSearchParams();
  for (const status of filters.statuses) params.append("status", status);
  for (const [name, value] of [
    ["from", filters.from],
    ["to", filters.to],
    ["currency", filters.currency],
    ["min", filters.min],
    ["max", filters.max],
    ["reference", filters.reference],
  ] as const) {
    if (value) params.set(name, value);
  }
  if (cursor) params.set("cursor", cursor);
  const search = params.toString();
  return search ? `/payments?${search}` : "/payments";
}
