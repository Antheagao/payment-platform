import { describe, expect, it, vi } from "vitest";
import { listPayments, PaymentsUnavailableError } from "./payments-api";

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
