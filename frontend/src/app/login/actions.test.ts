import { beforeEach, describe, expect, it, vi } from "vitest";
import { signInAction, signOutAction } from "./actions";

const { signIn, saveToken, clearToken, redirect } = vi.hoisted(() => ({
  signIn: vi.fn(),
  saveToken: vi.fn(),
  clearToken: vi.fn(),
  // Next.js's redirect throws to end the action; the test does the same and reads where it went.
  redirect: vi.fn((target: string) => {
    throw new Error(`redirect:${target}`);
  }),
}));

vi.mock("@/lib/auth-api", () => ({ signIn }));
vi.mock("@/lib/session", () => ({ saveToken, clearToken }));
vi.mock("next/navigation", () => ({ redirect }));

function form(fields: Record<string, string>) {
  const data = new FormData();
  for (const [name, value] of Object.entries(fields)) data.set(name, value);
  return data;
}

const empty = { email: "" };

describe("signInAction", () => {
  beforeEach(() => {
    signIn.mockReset();
  });

  it("saves the token and goes where the browser asked, if that is on this site", async () => {
    const expiresAt = new Date("2026-10-29T12:30:00Z");
    signIn.mockResolvedValue({ kind: "signed_in", token: "token-1", expiresAt });

    await expect(
      signInAction(
        empty,
        form({
          email: " demo@example.com ",
          password: "secret",
          next: "/payments?status=captured",
        }),
      ),
    ).rejects.toThrow("redirect:/payments?status=captured");

    expect(signIn).toHaveBeenCalledWith("demo@example.com", "secret");
    expect(saveToken).toHaveBeenCalledWith("token-1", expiresAt);
  });

  it("goes home instead of to another site", async () => {
    signIn.mockResolvedValue({ kind: "signed_in", token: "token-1", expiresAt: new Date() });

    await expect(
      signInAction(
        empty,
        form({ email: "demo@example.com", password: "secret", next: "//evil.example/" }),
      ),
    ).rejects.toThrow("redirect:/");
  });

  it.each([
    [{ email: "", password: "x" }, "email_required", "email"],
    [{ email: "not-an-email", password: "x" }, "email_invalid", "email"],
    [{ email: "demo@example.com", password: "" }, "password_required", "password"],
  ])("checks the form before calling the API: %j", async (fields, error, field) => {
    await expect(signInAction(empty, form(fields))).resolves.toMatchObject({
      error,
      field,
      email: fields.email,
    });
    expect(signIn).not.toHaveBeenCalled();
  });

  it.each([
    [{ kind: "invalid_credentials" }, { error: "invalid_credentials" }],
    [
      { kind: "rate_limited", retryAfterSeconds: 30 },
      { error: "rate_limited", retryAfterSeconds: 30 },
    ],
    [{ kind: "unavailable" }, { error: "unavailable" }],
    [
      { kind: "invalid_request", fields: { email: ["email_invalid"] } },
      { error: "email_invalid", field: "email" },
    ],
  ])("keeps the email and says why when the API refuses: %j", async (result, state) => {
    signIn.mockResolvedValue(result);

    await expect(
      signInAction(empty, form({ email: "demo@example.com", password: "wrong" })),
    ).resolves.toEqual({ ...state, email: "demo@example.com" });
    expect(saveToken).not.toHaveBeenCalled();
  });
});

describe("signOutAction", () => {
  it("forgets the token and goes to the login page", async () => {
    await expect(signOutAction()).rejects.toThrow("redirect:/login");
    expect(clearToken).toHaveBeenCalled();
  });
});
