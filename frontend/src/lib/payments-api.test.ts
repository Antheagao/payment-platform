import { describe, expect, it, vi } from "vitest";
import { getPayment, listPayments, movePayment, PaymentsUnavailableError } from "./payments-api";

const { readToken, redirect } = vi.hoisted(() => ({
  readToken: vi.fn(),
  redirect: vi.fn((target: string) => {
    throw new Error(`redirect:${target}`);
  }),
}));
vi.mock("@/lib/session", () => ({ readToken }));
vi.mock("next/navigation", () => ({ redirect }));

function answer(status: number, body: unknown) {
  const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify(body), { status }));
  vi.stubGlobal("fetch", fetch);
  return fetch;
}

const summary = {
  id: "019a2c00-0000-7000-8000-000000001025",
  status: "authorized",
  amount_minor: 14200,
  currency: "EUR",
  merchant_reference: "INV-1025",
  card: { brand: "visa", last4: "4242" },
  created_at: "2026-10-25T13:59:00+00:00",
  updated_at: "2026-10-25T13:59:02+00:00",
};

describe("listPayments", () => {
  it("asks for the page with the session's token and reads it", async () => {
    readToken.mockResolvedValue("token-1");
    const fetch = answer(200, { data: [summary], next_cursor: "next" });

    const page = await listPayments(new URLSearchParams("status=authorized"), "/payments");

    expect(fetch.mock.calls[0][0]).toBe("http://localhost:5080/api/v1/payments?status=authorized");
    expect(fetch.mock.calls[0][1].headers).toEqual({ authorization: "Bearer token-1" });
    expect(page).toEqual({
      kind: "page",
      nextCursor: "next",
      payments: [
        {
          id: summary.id,
          status: "authorized",
          amountMinor: 14200,
          currency: "EUR",
          merchantReference: "INV-1025",
          card: { brand: "visa", last4: "4242" },
          createdAt: summary.created_at,
          updatedAt: summary.updated_at,
        },
      ],
    });
  });

  it("sends a browser without a session to sign in, coming back to the same view", async () => {
    readToken.mockResolvedValue(undefined);

    await expect(listPayments(new URLSearchParams(), "/payments?status=failed")).rejects.toThrow(
      "redirect:/login?next=%2Fpayments%3Fstatus%3Dfailed",
    );
  });

  it("sends an ended session to sign in again, saying why", async () => {
    readToken.mockResolvedValue("expired");
    answer(401, { code: "token_expired" });

    await expect(listPayments(new URLSearchParams(), "/payments")).rejects.toThrow(
      "redirect:/login?next=%2Fpayments&reason=expired",
    );
  });

  it("returns the API's field codes for a request it refused", async () => {
    readToken.mockResolvedValue("token-1");
    answer(400, { code: "invalid_request", errors: { cursor: ["invalid_cursor"] } });

    await expect(listPayments(new URLSearchParams("cursor=x"), "/payments")).resolves.toEqual({
      kind: "invalid",
      fields: { cursor: ["invalid_cursor"] },
    });
  });

  it("throws for the error page when the API is down or answers with an error", async () => {
    readToken.mockResolvedValue("token-1");
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("fetch failed")));
    await expect(listPayments(new URLSearchParams(), "/payments")).rejects.toBeInstanceOf(
      PaymentsUnavailableError,
    );

    answer(503, { code: "service_unavailable" });
    await expect(listPayments(new URLSearchParams(), "/payments")).rejects.toBeInstanceOf(
      PaymentsUnavailableError,
    );
  });
});

const detail = {
  ...summary,
  card: { brand: "visa", last4: "4242", exp_month: 12, exp_year: 2028 },
  authorization_reference: "sim_auth_019a2c00000070008000000000001025",
  failure_reason: null,
  history: [
    {
      from: null,
      to: "pending",
      actor: { kind: "merchant_user", id: "019a2c00-0000-7000-8000-0000000000d2" },
      occurred_at: "2026-10-25T13:59:00+00:00",
      reason: null,
      correlation_id: "0af7651916cd43dd8448eb211c803190",
    },
    {
      from: "pending",
      to: "authorized",
      actor: { kind: "merchant_user", id: "019a2c00-0000-7000-8000-0000000000d2" },
      occurred_at: "2026-10-25T13:59:02+00:00",
      reason: null,
      correlation_id: null,
    },
  ],
};

describe("getPayment", () => {
  it("reads one payment, its card's expiry, and its history, oldest first", async () => {
    readToken.mockResolvedValue("token-1");
    const fetch = answer(200, detail);

    const payment = await getPayment(summary.id, `/payments/${summary.id}`);

    expect(fetch.mock.calls[0][0]).toBe(`http://localhost:5080/api/v1/payments/${summary.id}`);
    expect(fetch.mock.calls[0][1].headers).toEqual({ authorization: "Bearer token-1" });
    expect(payment).toMatchObject({
      id: summary.id,
      card: { brand: "visa", last4: "4242", expMonth: 12, expYear: 2028 },
      authorizationReference: detail.authorization_reference,
      failureReason: null,
      history: [
        {
          from: null,
          to: "pending",
          occurredAt: "2026-10-25T13:59:00+00:00",
          correlationId: "0af7651916cd43dd8448eb211c803190",
        },
        { from: "pending", to: "authorized", reason: null, correlationId: null },
      ],
    });
  });

  it("is null for an id the merchant has no payment with, which may be another merchant's", async () => {
    readToken.mockResolvedValue("token-1");
    answer(404, { code: "not_found" });

    await expect(getPayment(summary.id, "/payments/x")).resolves.toBeNull();
  });

  it("sends an ended session to sign in again, back to this payment", async () => {
    readToken.mockResolvedValue("expired");
    answer(401, { code: "token_expired" });

    await expect(getPayment(summary.id, `/payments/${summary.id}`)).rejects.toThrow(
      `redirect:/login?next=%2Fpayments%2F${summary.id}&reason=expired`,
    );
  });

  it("throws for the error page when the API is down or sends something else", async () => {
    readToken.mockResolvedValue("token-1");
    answer(503, { code: "service_unavailable" });
    await expect(getPayment(summary.id, "/payments/x")).rejects.toBeInstanceOf(
      PaymentsUnavailableError,
    );

    answer(200, { id: summary.id });
    await expect(getPayment(summary.id, "/payments/x")).rejects.toBeInstanceOf(
      PaymentsUnavailableError,
    );
  });
});

describe("movePayment", () => {
  it("posts the move under the form's idempotency key and reads the moved payment", async () => {
    readToken.mockResolvedValue("token-1");
    const fetch = answer(200, { ...detail, status: "captured" });

    const result = await movePayment(summary.id, "capture", "key-1", "/payments/x");

    expect(fetch.mock.calls[0][0]).toBe(
      `http://localhost:5080/api/v1/payments/${summary.id}/capture`,
    );
    expect(fetch.mock.calls[0][1]).toMatchObject({
      method: "POST",
      headers: { authorization: "Bearer token-1", "idempotency-key": "key-1" },
    });
    expect(result).toMatchObject({ kind: "moved", payment: { status: "captured" } });
  });

  it("returns the refusal's code, and how long to wait when the API says", async () => {
    readToken.mockResolvedValue("token-1");
    answer(409, { code: "payment_not_refundable" });
    await expect(movePayment(summary.id, "refund", "key-1", "/payments/x")).resolves.toEqual({
      kind: "refused",
      code: "payment_not_refundable",
      retryAfterSeconds: undefined,
    });

    const unavailable = new Response(JSON.stringify({ code: "acquirer_unavailable" }), {
      status: 503,
      headers: { "retry-after": "5" },
    });
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(unavailable));
    await expect(movePayment(summary.id, "capture", "key-1", "/payments/x")).resolves.toEqual({
      kind: "refused",
      code: "acquirer_unavailable",
      retryAfterSeconds: 5,
    });
  });

  it("is unavailable when the API does not answer, or answers without a code", async () => {
    readToken.mockResolvedValue("token-1");
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("fetch failed")));
    await expect(movePayment(summary.id, "capture", "key-1", "/payments/x")).resolves.toEqual({
      kind: "unavailable",
    });

    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("Bad gateway", { status: 502 })));
    await expect(movePayment(summary.id, "capture", "key-1", "/payments/x")).resolves.toEqual({
      kind: "unavailable",
    });
  });

  it("sends an ended session to sign in again", async () => {
    readToken.mockResolvedValue("expired");
    answer(401, { code: "token_expired" });

    await expect(movePayment(summary.id, "refund", "key-1", "/payments/x")).rejects.toThrow(
      "redirect:/login?next=%2Fpayments%2Fx&reason=expired",
    );
  });
});
