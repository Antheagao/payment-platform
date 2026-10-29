import { beforeEach, describe, expect, it, vi } from "vitest";
import { moveAction } from "./actions";

const { movePayment, refresh } = vi.hoisted(() => ({ movePayment: vi.fn(), refresh: vi.fn() }));
vi.mock("@/lib/payments-api", () => ({ movePayment }));
vi.mock("next/cache", () => ({ refresh }));

const id = "019a2c00-0000-7000-8000-000000001025";

function form(fields: Record<string, string>) {
  const data = new FormData();
  for (const [name, value] of Object.entries(fields)) data.set(name, value);
  return data;
}

const capture = form({ id, operation: "capture", idempotency_key: "key-1" });
const refund = form({ id, operation: "refund", idempotency_key: "key-1" });

describe("moveAction", () => {
  beforeEach(() => {
    movePayment.mockReset();
    refresh.mockReset();
  });

  it("moves the payment under the form's key, then refreshes the page to show it", async () => {
    movePayment.mockResolvedValue({ kind: "moved", payment: {} });

    await expect(moveAction({}, capture)).resolves.toEqual({
      tone: "success",
      message: "Captured. The funds are claimed and settle with the next batch.",
    });
    expect(movePayment).toHaveBeenCalledWith(id, "capture", "key-1", `/payments/${id}`);
    expect(refresh).toHaveBeenCalledOnce();
  });

  it.each([
    { id: "not-a-guid", operation: "capture", idempotency_key: "key-1" },
    { id, operation: "void", idempotency_key: "key-1" },
    { id, operation: "refund", idempotency_key: "" },
  ])("refuses a form the page did not send: %o", async (fields) => {
    await expect(moveAction({}, form(fields))).resolves.toMatchObject({ tone: "error" });
    expect(movePayment).not.toHaveBeenCalled();
  });

  it.each([
    ["payment_not_capturable", "can no longer be captured: it is not authorized any more."],
    ["payment_not_refundable", "can no longer be refunded"],
  ])(
    "says what %s means, and refreshes the page to show where the payment is now",
    async (code, words) => {
      movePayment.mockResolvedValue({ kind: "refused", code });

      const state = await moveAction({}, code === "payment_not_refundable" ? refund : capture);

      expect(state.tone).toBe("error");
      expect(state.message).toContain(words);
      expect(refresh).toHaveBeenCalledOnce();
    },
  );

  it.each([
    ["capture_declined", undefined, "The card network declined the capture. Nothing changed."],
    ["acquirer_unavailable", 5, "Nothing changed; try again in 5 seconds."],
    ["acquirer_unavailable", undefined, "Nothing changed; try again."],
    ["idempotency_request_in_progress", undefined, "still being processed"],
    ["not_found", undefined, "not one of yours"],
    ["idempotency_key_reused", undefined, "refused the capture (idempotency_key_reused)"],
  ])("says what %s means, and leaves the page as it is", async (code, wait, words) => {
    movePayment.mockResolvedValue({ kind: "refused", code, retryAfterSeconds: wait });

    const state = await moveAction({}, capture);

    expect(state.tone).toBe("error");
    expect(state.message).toContain(words);
    expect(refresh).not.toHaveBeenCalled();
  });

  it("says nothing changed when the API did not answer", async () => {
    movePayment.mockResolvedValue({ kind: "unavailable" });

    await expect(moveAction({}, refund)).resolves.toEqual({
      tone: "error",
      message: "The payments service did not answer. Nothing changed; try the refund again.",
    });
  });
});
