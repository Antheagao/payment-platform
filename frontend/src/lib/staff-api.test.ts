import { describe, expect, it, vi } from "vitest";
import { listDeadLetters, replayDelivery, StaffApiUnavailableError } from "./staff-api";

const { readStaffToken, redirect } = vi.hoisted(() => ({
  readStaffToken: vi.fn(),
  redirect: vi.fn((target: string) => {
    throw new Error(`redirect:${target}`);
  }),
}));
vi.mock("@/lib/staff-session", () => ({ readStaffToken, staffArea: "/operator" }));
vi.mock("next/navigation", () => ({ redirect }));

function answer(status: number, body: unknown) {
  const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify(body), { status }));
  vi.stubGlobal("fetch", fetch);
  return fetch;
}

const row = {
  id: 37,
  merchant_id: "019a2c00-0000-7000-8000-0000000000d1",
  payment_id: "019a2c00-0000-7000-8000-000000001280",
  event_type: "payment.settled",
  attempts: 12,
  last_status_code: 503,
  last_error: "HTTP 503",
  endpoint_host: "webhook-receiver",
  created_at: "2026-10-30T01:32:08+00:00",
};

describe("listDeadLetters", () => {
  it("asks for the dead letters with the staff token and reads them", async () => {
    readStaffToken.mockResolvedValue("staff-token");
    const fetch = answer(200, { data: [row], next_cursor: null });

    const rows = await listDeadLetters("/operator");

    expect(fetch.mock.calls[0][0]).toBe(
      "http://localhost:5080/api/v1/staff/webhook-deliveries?status=dead_lettered",
    );
    expect(fetch.mock.calls[0][1].headers).toEqual({ authorization: "Bearer staff-token" });
    expect(rows).toEqual([
      {
        id: 37,
        merchantId: row.merchant_id,
        paymentId: row.payment_id,
        eventType: "payment.settled",
        attempts: 12,
        lastStatusCode: 503,
        lastError: "HTTP 503",
        endpointHost: "webhook-receiver",
        createdAt: row.created_at,
      },
    ]);
  });

  it("sends a browser with no staff session to the staff sign-in, and an ended one with the reason", async () => {
    readStaffToken.mockResolvedValue(undefined);
    await expect(listDeadLetters("/operator")).rejects.toThrow(
      "redirect:/operator/login?next=%2Foperator",
    );

    readStaffToken.mockResolvedValue("ended");
    answer(401, { code: "token_expired" });
    await expect(listDeadLetters("/operator")).rejects.toThrow(
      "redirect:/operator/login?next=%2Foperator&reason=expired",
    );
  });

  it("throws for the error page when the API is down or answers with an error", async () => {
    readStaffToken.mockResolvedValue("staff-token");
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("fetch failed")));
    await expect(listDeadLetters("/operator")).rejects.toBeInstanceOf(StaffApiUnavailableError);

    answer(500, { code: "internal_error" });
    await expect(listDeadLetters("/operator")).rejects.toBeInstanceOf(StaffApiUnavailableError);
  });
});

describe("replayDelivery", () => {
  it("posts the replay and reports it", async () => {
    readStaffToken.mockResolvedValue("staff-token");
    const fetch = answer(200, { id: 37, status: "pending", attempts: 0 });

    await expect(replayDelivery(37, "/operator")).resolves.toEqual({ kind: "replayed" });
    expect(fetch.mock.calls[0][0]).toBe(
      "http://localhost:5080/api/v1/staff/webhook-deliveries/37/replay",
    );
    expect(fetch.mock.calls[0][1]).toMatchObject({ method: "POST" });
  });

  it("returns the refusal's code, and is unavailable when there is none", async () => {
    readStaffToken.mockResolvedValue("staff-token");
    answer(409, { code: "delivery_not_dead_lettered" });
    await expect(replayDelivery(37, "/operator")).resolves.toEqual({
      kind: "refused",
      code: "delivery_not_dead_lettered",
    });

    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("Bad gateway", { status: 502 })));
    await expect(replayDelivery(37, "/operator")).resolves.toEqual({ kind: "unavailable" });
  });
});
