import { describe, expect, it } from "vitest";
import { safeRedirectTarget } from "./redirect-target";

describe("safeRedirectTarget", () => {
  it.each(["/", "/payments", "/payments?status=captured&cursor=abc", "/payments/019a2c00#history"])(
    "keeps a path on this site: %s",
    (path) => {
      expect(safeRedirectTarget(path)).toBe(path);
    },
  );

  it.each([
    undefined,
    null,
    42,
    "",
    "payments",
    "https://evil.example/",
    "//evil.example/",
    "/\\evil.example",
    "\\\\evil.example",
    "/payments\nSet-Cookie: x",
    "/payments\u0000",
    "/login",
    "/login?next=/login",
    "/login/again",
  ])("sends anything else home: %j", (value) => {
    expect(safeRedirectTarget(value)).toBe("/");
  });
});
