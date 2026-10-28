import { describe, expect, it } from "vitest";
import { classNames } from "./class-names";

describe("classNames", () => {
  it("joins names with single spaces", () => {
    expect(classNames("a", "b", "c")).toBe("a b c");
  });

  it("skips empty, false, null, and undefined values", () => {
    expect(classNames("a", "", false, null, undefined, "b")).toBe("a b");
  });

  it("returns an empty string when nothing is left", () => {
    expect(classNames(false, undefined)).toBe("");
  });
});
