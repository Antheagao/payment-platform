import { describe, expect, it } from "vitest";
import { brandName, describeChange, describeReason, formatInstant } from "./payment-display";
import type { CardBrand, PaymentHistoryEntry } from "./payments-api";

const user = { kind: "merchant_user", id: "019a2c00-0000-7000-8000-0000000000d2" } as const;

function entry(change: Partial<PaymentHistoryEntry>): PaymentHistoryEntry {
  return {
    from: null,
    to: "pending",
    actor: user,
    occurredAt: "2026-10-25T13:59:00+00:00",
    reason: null,
    correlationId: null,
    ...change,
  };
}

describe("describeChange", () => {
  it("says a payment was created, and by which user", () => {
    expect(describeChange(entry({}))).toEqual({
      title: "Created as pending",
      by: "Merchant user 019a2c00",
      reason: undefined,
    });
  });

  it("names both statuses of a move, and the job that made a system one", () => {
    expect(
      describeChange(
        entry({ from: "captured", to: "settled", actor: { kind: "system", id: "settlement" } }),
      ),
    ).toEqual({ title: "Captured to settled", by: "The settlement job", reason: undefined });
  });

  it("puts a known decline in words, and shows an unknown one as its code", () => {
    const failed = entry({ from: "pending", to: "failed", reason: "card_declined" });
    expect(describeChange(failed).reason).toBe("The card was declined.");
    expect(describeReason("insufficient_funds")).toBe("The card had insufficient funds.");
    expect(describeReason("do_not_honor")).toBe("do_not_honor");
  });
});

describe("formatInstant", () => {
  it("writes the time in UTC and says so, whatever the machine's zone", () => {
    expect(formatInstant("2026-10-25T13:59:02-07:00")).toBe("Oct 25, 2026, 8:59 PM UTC");
  });
});

describe("brandName", () => {
  it("names each card brand as it brands itself", () => {
    const brands: CardBrand[] = ["visa", "mastercard", "amex", "discover"];
    expect(brands.map(brandName)).toEqual(["Visa", "Mastercard", "Amex", "Discover"]);
  });
});
