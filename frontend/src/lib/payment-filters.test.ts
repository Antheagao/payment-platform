import { describe, expect, it } from "vitest";
import { pageHref, parseFilters } from "./payment-filters";

describe("parseFilters", () => {
  it("is the newest payments, unfiltered, with no parameters", () => {
    const parsed = parseFilters({});

    expect(parsed.query?.toString()).toBe("");
    expect(parsed).toMatchObject({ errors: {}, filtered: false });
  });

  it("builds the API's query from every filter, in minor units and with an exclusive end date", () => {
    const parsed = parseFilters({
      status: ["captured", "settled"],
      from: "2026-10-01",
      to: "2026-10-31",
      currency: "USD",
      min: "10",
      max: "250.50",
      reference: "INV-10",
    });

    expect(parsed.errors).toEqual({});
    expect(Object.fromEntries(parsed.query!)).toEqual({
      status: "captured,settled",
      created_from: "2026-10-01",
      created_to: "2026-11-01",
      currency: "USD",
      amount_min_minor: "1000",
      amount_max_minor: "25050",
      reference: "INV-10",
    });
    expect(parsed.filtered).toBe(true);
  });

  it("reads an amount in yen without a minor unit", () => {
    expect(parseFilters({ currency: "JPY", min: "7,600" }).query?.get("amount_min_minor")).toBe(
      "7600",
    );
  });

  it("takes a comma list or repeated statuses, once each", () => {
    expect(parseFilters({ status: "failed,refunded" }).filters.statuses).toEqual([
      "failed",
      "refunded",
    ]);
    expect(parseFilters({ status: ["failed", "failed"] }).filters.statuses).toEqual(["failed"]);
  });

  it("rolls the end date over a month and a leap day", () => {
    expect(parseFilters({ to: "2026-12-31" }).query?.get("created_to")).toBe("2027-01-01");
    expect(parseFilters({ to: "2028-02-28" }).query?.get("created_to")).toBe("2028-02-29");
  });

  it.each([
    [{ status: "lost" }, "status"],
    [{ from: "10/01/2026" }, "from"],
    [{ to: "2026-02-30" }, "to"],
    [{ from: "2026-10-31", to: "2026-10-01" }, "to"],
    [{ currency: "CHF" }, "currency"],
    [{ min: "10" }, "min"],
    [{ max: "10" }, "max"],
    [{ currency: "USD", min: "ten" }, "min"],
    [{ currency: "USD", min: "1.234" }, "min"],
    [{ currency: "JPY", max: "12.5" }, "max"],
    [{ currency: "USD", min: "50", max: "10" }, "max"],
    [{ reference: "x".repeat(101) }, "reference"],
  ])("refuses %j with a message on %s, and sends nothing to the API", (params, field) => {
    const parsed = parseFilters(params);

    expect(parsed.query).toBeNull();
    expect(Object.keys(parsed.errors)).toEqual([field]);
  });

  it("keeps what was typed, so the form shows it back", () => {
    const parsed = parseFilters({ currency: "USD", min: "ten", reference: " INV " });

    expect(parsed.filters).toMatchObject({ currency: "USD", min: "ten", reference: "INV" });
  });
});

describe("pageHref", () => {
  it("is the page's own address for the filters and a cursor", () => {
    const { filters } = parseFilters({
      status: ["captured", "settled"],
      currency: "USD",
      min: "10",
    });

    expect(pageHref(filters)).toBe("/payments?status=captured&status=settled&currency=USD&min=10");
    expect(pageHref(filters, "abc")).toBe(
      "/payments?status=captured&status=settled&currency=USD&min=10&cursor=abc",
    );
    expect(pageHref(parseFilters({}).filters)).toBe("/payments");
  });
});
