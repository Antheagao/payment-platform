import { describe, expect, it, vi } from "vitest";
import { getCurrentStaff, getCurrentUser, signIn, staffSignIn } from "./auth-api";

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
    expect(init.headers).toEqual({ "content-type": "application/json" });
  });

  it("names the person's address under the dashboard's key, and only with both", async () => {
    vi.stubEnv("DASHBOARD_KEY", "local-dashboard-key");
    const fetch = answer(401, problem("invalid_credentials"));

    await signIn("a@example.com", "x", "203.0.113.7");
    await signIn("a@example.com", "x");
    vi.stubEnv("DASHBOARD_KEY", "");
    await signIn("a@example.com", "x", "203.0.113.7");
    vi.unstubAllEnvs();

    expect(fetch.mock.calls.map(([, init]) => init.headers)).toEqual([
      {
        "content-type": "application/json",
        "x-client-address": "203.0.113.7",
        "x-dashboard-key": "local-dashboard-key",
      },
      { "content-type": "application/json" },
      { "content-type": "application/json" },
    ]);
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

describe("staff (ADR 011)", () => {
  it("signs staff in on their own route, with the same answers", async () => {
    const fetch = answer(200, {
      access_token: "staff-token",
      token_type: "Bearer",
      expires_at: "2026-10-29T12:30:00+00:00",
      expires_in: 1800,
    });

    await expect(staffSignIn("operator@example.com", "secret")).resolves.toMatchObject({
      kind: "signed_in",
      token: "staff-token",
    });
    expect(fetch.mock.calls[0][0]).toBe("http://localhost:5080/api/v1/staff/auth/login");
  });

  it("names the staff user, and calls a refused token signed out", async () => {
    answer(200, {
      staff_id: "019a2c00-0000-7000-8000-0000000000d4",
      email: "operator@example.com",
    });
    await expect(getCurrentStaff("staff-token")).resolves.toEqual({
      kind: "staff",
      staff: { staffId: "019a2c00-0000-7000-8000-0000000000d4", email: "operator@example.com" },
    });

    // A merchant's token on the staff route is 403: no session here, not an outage.
    answer(403, { code: "forbidden" });
    await expect(getCurrentStaff("merchant-token")).resolves.toEqual({
      kind: "signed_out",
      expired: false,
    });

    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("fetch failed")));
    await expect(getCurrentStaff("staff-token")).resolves.toEqual({ kind: "unavailable" });
  });
});
