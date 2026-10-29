import { describe, expect, it, vi } from "vitest";
import { clearToken, readToken, saveToken, sessionCookie } from "./session";

const jar = vi.hoisted(() => ({ get: vi.fn(), set: vi.fn(), delete: vi.fn() }));
vi.mock("next/headers", () => ({ cookies: () => Promise.resolve(jar) }));

describe("session cookie", () => {
  it("is HttpOnly, Secure, SameSite=Strict, site-wide, and ends with the token (ADR 004)", async () => {
    const expiresAt = new Date("2026-10-29T12:30:00Z");

    await saveToken("token-1", expiresAt);

    expect(jar.set).toHaveBeenCalledWith(sessionCookie, "token-1", {
      httpOnly: true,
      secure: true,
      sameSite: "strict",
      path: "/",
      expires: expiresAt,
    });
  });

  it("reads the token, and nothing for a missing or empty cookie", async () => {
    jar.get
      .mockReturnValueOnce({ value: "token-1" })
      .mockReturnValueOnce(undefined)
      .mockReturnValueOnce({ value: "" });

    await expect(readToken()).resolves.toBe("token-1");
    await expect(readToken()).resolves.toBeUndefined();
    await expect(readToken()).resolves.toBeUndefined();
  });

  it("is deleted on sign-out", async () => {
    await clearToken();

    expect(jar.delete).toHaveBeenCalledWith(sessionCookie);
  });
});
