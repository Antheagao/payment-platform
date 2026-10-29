import type { CardBrand, PaymentHistoryEntry } from "@/lib/payments-api";
import { paymentStatusDisplay } from "@/lib/payment-status";

const brandNames: Record<CardBrand, string> = {
  visa: "Visa",
  mastercard: "Mastercard",
  amex: "Amex",
  discover: "Discover",
};

export function brandName(brand: CardBrand): string {
  return brandNames[brand] ?? brand;
}

// UTC, written out, so a time reads the same on every machine and matches the API's.
const instantFormat = new Intl.DateTimeFormat("en-US", {
  dateStyle: "medium",
  timeStyle: "short",
  timeZone: "UTC",
});

export function formatInstant(iso: string): string {
  return `${instantFormat.format(new Date(iso))} UTC`;
}

// The decline codes the acquirer gives, in words; any other code is shown as it is.
const reasons: Record<string, string> = {
  card_declined: "The card was declined.",
  insufficient_funds: "The card had insufficient funds.",
};

/** A decline code as a sentence, or the code itself when it is not one the dashboard knows. */
export function describeReason(code: string): string {
  return reasons[code] ?? code;
}

/** One history entry as a sentence: what happened, and who did it. */
export function describeChange(entry: PaymentHistoryEntry): {
  title: string;
  by: string;
  reason?: string;
} {
  const to = paymentStatusDisplay[entry.to].label;
  const title =
    entry.from === null
      ? `Created as ${to.toLowerCase()}`
      : `${paymentStatusDisplay[entry.from].label} to ${to.toLowerCase()}`;
  const by =
    entry.actor.kind === "system"
      ? `The ${entry.actor.id} job`
      : `Merchant user ${entry.actor.id.slice(0, 8)}`;
  return { title, by, reason: entry.reason ? describeReason(entry.reason) : undefined };
}
