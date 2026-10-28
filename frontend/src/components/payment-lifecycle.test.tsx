import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { PaymentLifecycle } from "./payment-lifecycle";

describe("PaymentLifecycle", () => {
  it("is a labelled region listing the happy path in order", () => {
    render(<PaymentLifecycle />);

    const region = screen.getByRole("region", { name: "Payment lifecycle" });
    const steps = within(within(region).getAllByRole("list")[0]).getAllByRole("listitem");
    expect(steps.map((step) => step.textContent)).toEqual([
      expect.stringContaining("Pending"),
      expect.stringContaining("Authorized"),
      expect.stringContaining("Captured"),
      expect.stringContaining("Settled"),
    ]);
  });

  it("shows the two terminal branches separately", () => {
    render(<PaymentLifecycle />);

    const branches = within(screen.getAllByRole("list")[1]).getAllByRole("listitem");
    expect(branches).toHaveLength(2);
    expect(branches[0]).toHaveTextContent("Failed");
    expect(branches[1]).toHaveTextContent("Refunded");
  });
});
