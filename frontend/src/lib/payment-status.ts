import type { Tone } from "@/components/ui/badge";

/** A payment's lifecycle state as the API spells it (snake_case enum values, ADR 006). */
export type PaymentStatus =
  "pending" | "authorized" | "captured" | "settled" | "failed" | "refunded";

/** How each status reads on screen. A `Record` over the union, so a new status fails `tsc` until it has a row. */
export const paymentStatusDisplay: Record<
  PaymentStatus,
  { label: string; tone: Tone; meaning: string }
> = {
  pending: {
    label: "Pending",
    tone: "warning",
    meaning: "Recorded; the card network has not answered yet.",
  },
  authorized: { label: "Authorized", tone: "info", meaning: "Approved; the funds are reserved." },
  captured: { label: "Captured", tone: "primary", meaning: "Claimed; waiting for settlement." },
  settled: { label: "Settled", tone: "success", meaning: "Paid out to the merchant." },
  failed: { label: "Failed", tone: "danger", meaning: "Declined; nothing was charged." },
  refunded: { label: "Refunded", tone: "neutral", meaning: "Returned to the cardholder." },
};

/** The happy path, in order; failed and refunded branch off it. */
export const lifecycleOrder: readonly PaymentStatus[] = [
  "pending",
  "authorized",
  "captured",
  "settled",
];
