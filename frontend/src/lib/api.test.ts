import { describe, expect, it, vi } from "vitest";
import { getApiHealth } from "./api";

describe("getApiHealth", () => {
  it("is ready when the readiness probe answers 2xx", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("{}", { status: 200 })));

    await expect(getApiHealth()).resolves.toBe("ready");
  });

  it("is not ready when the probe answers 503", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("{}", { status: 503 })));

    await expect(getApiHealth()).resolves.toBe("not_ready");
  });

  it("is unreachable instead of throwing when the request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("fetch failed")));

    await expect(getApiHealth()).resolves.toBe("unreachable");
  });

  it("asks the readiness probe uncached and with a timeout", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response("{}", { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await getApiHealth();

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toMatch(/\/health\/ready$/);
    expect(init.cache).toBe("no-store");
    expect(init.signal).toBeInstanceOf(AbortSignal);
  });
});
