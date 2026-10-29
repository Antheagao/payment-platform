import { describe, expect, it, vi } from "vitest";
import { getCurrentUser, signIn } from "./auth-api";

vi.mock("next/server", () => ({ connection: () => Promise.resolve() }));

function answer(status: number, body: unknown, headers: Record<string, string> = {}) {
  const fetch = vi.fn().mockResolvedValue(
    new Response(JSON.stringify(body), {
      status,
      headers: { "content-type": "application/json", ...headers },
    }),
  );
  vi.stubGlobal("fetch", fetch);
  return fetch;
}

const problem = (code: string, extra: object = {}) => ({ code, title: "x", status: 0, ...extra });

describe("signIn", () => {
  it("posts the credentials as JSON from the server and returns the token and its expiry", async () => {
    const fetch = answer(200, {
      access_token: "token-1",
      token_type: "Bearer",
      expires_at: "2026-10-29T12:30:00+00:00",
      expires_in: 1800,
    });

    const result = await signIn("demo@example.com", "secret");

    expect(result).toEqual({
      kind: "signed_in",
      token: "token-1",
      expiresAt: new Date("2026-10-29T12:30:00Z"),
    });
    const [url, init] = fetch.mock.calls[0];
    expect(url).toBe("http://localhost:5080/api/v1/auth/login");
    expect(init).toMatchObject({ method: "POST", cache: "no-store" });
    expect(JSON.parse(init.body)).toEqual({ email: "demo@example.com", password: "secret" });
  });

  it("reads each refusal by its code, not its text", async () => {
    answer(401, problem("invalid_credentials", { detail: "anything at all" }));
    await expect(signIn("a@example.com", "x")).resolves.toEqual({ kind: "invalid_credentials" });

    answer(429, problem("rate_limited"), { "retry-after": "17" });
    await expect(signIn("a@example.com", "x")).resolves.toEqual({
      kind: "rate_limited",
      retryAfterSeconds: 17,
    });

    answer(400, problem("invalid_request", { errors: { email: ["email_invalid"] } }));
    await expect(signIn("a", "x")).resolves.toEqual({
      kind: "invalid_request",
      fields: { email: ["email_invalid"] },
    });
  });

  it("is unavailable when the API does not answer, answers 5xx, or sends a token it cannot read", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("fetch failed")));
    await expect(signIn("a@example.com", "x")).resolves.toEqual({ kind: "unavailable" });

    answer(503, problem("service_unavailable"));
    await expect(signIn("a@example.com", "x")).resolves.toEqual({ kind: "unavailable" });

    answer(200, { access_token: "token-1", expires_at: "not a date" });
    await expect(signIn("a@example.com", "x")).resolves.toEqual({ kind: "unavailable" });
  });
});

describe("getCurrentUser", () => {
  it("asks /me with the token as a bearer token", async () => {
    const fetch = answer(200, {
      user_id: "u1",
      merchant_id: "m1",
      email: "demo@example.com",
      merchant_name: "Demo Merchant",
    });

    await expect(getCurrentUser("token-1")).resolves.toEqual({
      kind: "user",
      user: {
        userId: "u1",
        merchantId: "m1",
        email: "demo@example.com",
        merchantName: "Demo Merchant",
      },
    });
    expect(fetch.mock.calls[0][1].headers).toEqual({ authorization: "Bearer token-1" });
  });

  it("is signed out for a refused token, and says when it had expired", async () => {
    answer(401, problem("token_expired"));
    await expect(getCurrentUser("old")).resolves.toEqual({ kind: "signed_out", expired: true });

    answer(401, problem("unauthorized"));
    await expect(getCurrentUser("forged")).resolves.toEqual({ kind: "signed_out", expired: false });
  });

  it("is unavailable, not signed out, when the API is down", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("fetch failed")));
    await expect(getCurrentUser("token-1")).resolves.toEqual({ kind: "unavailable" });
  });
});
