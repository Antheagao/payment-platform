import { describe, expect, it } from "vitest";
import { lifecycleOrder, paymentStatusDisplay } from "./payment-status";

describe("paymentStatusDisplay", () => {
  it("gives every status its own label", () => {
    const labels = Object.values(paymentStatusDisplay).map((display) => display.label);

    expect(new Set(labels).size).toBe(labels.length);
  });

  it("never shows two statuses in the same tone, so color is a second cue after the label", () => {
    const tones = Object.values(paymentStatusDisplay).map((display) => display.tone);

    expect(new Set(tones).size).toBe(tones.length);
  });

  it("orders the happy path the way the API's state machine does", () => {
    expect(lifecycleOrder).toEqual(["pending", "authorized", "captured", "settled"]);
  });
});
