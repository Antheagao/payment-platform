/** The six currencies the API accepts (`Domain/Monetary/Currency`), in the order the filter lists them. */
export const currencies = ["USD", "EUR", "GBP", "CAD", "AUD", "JPY"] as const;

export type CurrencyCode = (typeof currencies)[number];

export function isCurrency(value: unknown): value is CurrencyCode {
  return typeof value === "string" && (currencies as readonly string[]).includes(value);
}

/** How many digits the currency's minor unit has: 2 for USD, 0 for JPY. */
export function minorDigits(currency: string): number {
  return (
    new Intl.NumberFormat("en-US", { style: "currency", currency }).resolvedOptions()
      .maximumFractionDigits ?? 2
  );
}

/** An amount in minor units, as the API stores it, shown in the currency's own form: `$25.99`, `¥7,600`. */
export function formatMoney(amountMinor: number, currency: string): string {
  const digits = minorDigits(currency);
  return new Intl.NumberFormat("en-US", {
    style: "currency",
    currency,
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  }).format(amountMinor / 10 ** digits);
}

/**
 * What a person typed as an amount (`25`, `25.9`, `1,250.00`), in the currency's minor units, or null when it is
 * not a plain non-negative amount with at most the currency's digits. Done on the digits, not on floating point,
 * so `0.29` is 29 cents and never 28.999.
 */
export function toMinorUnits(text: string, currency: string): number | null {
  const digits = minorDigits(currency);
  const match = /^(\d{1,3}(?:,\d{3})*|\d+)(?:\.(\d*))?$/.exec(text.trim());
  if (!match) return null;
  const fraction = match[2] ?? "";
  if (fraction.length > digits) return null;
  const whole = match[1].replaceAll(",", "");
  const minor = Number(`${whole}${fraction.padEnd(digits, "0")}`);
  return Number.isSafeInteger(minor) ? minor : null;
}
