import { beforeEach, describe, expect, it, vi } from "vitest";
import { replayAction } from "./actions";

const { replayDelivery, refresh } = vi.hoisted(() => ({
  replayDelivery: vi.fn(),
  refresh: vi.fn(),
}));
vi.mock("@/lib/staff-api", () => ({ replayDelivery }));
vi.mock("next/cache", () => ({ refresh }));

function form(id: string) {
  const data = new FormData();
  data.set("id", id);
  return data;
}

describe("replayAction", () => {
  beforeEach(() => {
    replayDelivery.mockReset();
    refresh.mockReset();
  });

  it("replays the delivery and refreshes the list, which no longer holds it", async () => {
    replayDelivery.mockResolvedValue({ kind: "replayed" });

    await expect(replayAction({}, form("37"))).resolves.toEqual({
      tone: "success",
      message: "Delivery 37 is back on the queue; the worker sends it within a few seconds.",
    });
    expect(replayDelivery).toHaveBeenCalledWith(37, "/operator");
    expect(refresh).toHaveBeenCalledOnce();
  });

  it.each(["", "0", "-1", "1.5", "abc"])("refuses an id the page did not send: %j", async (id) => {
    await expect(replayAction({}, form(id))).resolves.toMatchObject({ tone: "error" });
    expect(replayDelivery).not.toHaveBeenCalled();
  });

  it.each([
    ["delivery_not_dead_lettered", "replayed already"],
    ["webhook_endpoint_disabled", "endpoint is disabled"],
    ["not_found", "no longer exists"],
    ["something_new", "refused the replay (something_new)"],
  ])("says what %s means, and refreshes to show the list as it is", async (code, words) => {
    replayDelivery.mockResolvedValue({ kind: "refused", code });

    const state = await replayAction({}, form("37"));

    expect(state.tone).toBe("error");
    expect(state.message).toContain(words);
    expect(refresh).toHaveBeenCalledOnce();
  });

  it("says nothing changed when the API did not answer", async () => {
    replayDelivery.mockResolvedValue({ kind: "unavailable" });

    await expect(replayAction({}, form("37"))).resolves.toEqual({
      tone: "error",
      message: "The API did not answer. Nothing changed; try the replay again.",
    });
    expect(refresh).not.toHaveBeenCalled();
  });
});
