import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { Badge } from "./badge";

describe("Badge", () => {
  it("shows its label as text, with the dot hidden from assistive technology", () => {
    const { container } = render(<Badge tone="success">Settled</Badge>);

    expect(screen.getByText("Settled")).toBeVisible();
    expect(container.querySelector("[aria-hidden='true']")).not.toBeNull();
  });

  it("uses the tone's token classes", () => {
    render(<Badge tone="danger">Failed</Badge>);

    expect(screen.getByText("Failed")).toHaveClass("bg-danger-soft", "text-danger");
  });

  it("is neutral by default and merges the caller's classes", () => {
    render(<Badge className="ml-2">Refunded</Badge>);

    expect(screen.getByText("Refunded")).toHaveClass("bg-surface", "text-muted", "ml-2");
  });

  it("passes other attributes through", () => {
    render(
      <Badge role="status" tone="success">
        API ready
      </Badge>,
    );

    expect(screen.getByRole("status")).toHaveTextContent("API ready");
  });
});
