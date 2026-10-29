import { describe, expect, it } from "vitest";
import { formatMoney, isCurrency, minorDigits, toMinorUnits } from "./money";

describe("money", () => {
  it("knows each currency's minor unit", () => {
    expect(minorDigits("USD")).toBe(2);
    expect(minorDigits("EUR")).toBe(2);
    expect(minorDigits("JPY")).toBe(0);
  });

  it.each([
    [2599, "USD", "$25.99"],
    [100000, "USD", "$1,000.00"],
    [7600, "JPY", "¥7,600"],
    [5, "EUR", "€0.05"],
    [101207, "AUD", "A$1,012.07"],
    [63397, "CAD", "CA$633.97"],
    [32199, "GBP", "£321.99"],
  ])("shows %i minor units of %s as %s", (amount, currency, shown) => {
    expect(formatMoney(amount, currency)).toBe(shown);
  });

  it.each([
    ["25", "USD", 2500],
    ["25.9", "USD", 2590],
    ["0.29", "USD", 29],
    ["1,250.00", "USD", 125000],
    [" 12.50 ", "EUR", 1250],
    ["7600", "JPY", 7600],
    ["7,600", "JPY", 7600],
    ["0", "USD", 0],
  ])("reads %j in %s as %i minor units", (text, currency, minor) => {
    expect(toMinorUnits(text, currency)).toBe(minor);
  });

  it.each([
    ["", "USD"],
    ["abc", "USD"],
    ["-5", "USD"],
    ["1.234", "USD"],
    ["12.5", "JPY"],
    ["1,25", "USD"],
    ["1e3", "USD"],
    ["99999999999999999999", "USD"],
  ])("refuses %j in %s", (text, currency) => {
    expect(toMinorUnits(text, currency)).toBeNull();
  });

  it("accepts only the six currencies the API takes", () => {
    expect(["USD", "EUR", "GBP", "CAD", "AUD", "JPY"].every(isCurrency)).toBe(true);
    expect(isCurrency("CHF")).toBe(false);
    expect(isCurrency("usd")).toBe(false);
  });
});
