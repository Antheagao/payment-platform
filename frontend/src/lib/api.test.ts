import { describe, expect, it, vi } from "vitest";
import { getApiHealth, getWorkerHealth } from "./api";

// `connection()` only works inside a Next.js request; here there is no prerender to wait out.
vi.mock("next/server", () => ({ connection: () => Promise.resolve() }));

const healthy = JSON.stringify({
  status: "healthy",
  duration_ms: 5.2,
  checks: { database: "healthy", self: "healthy" },
});

function answer(body: string, status: number) {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(body, { status })));
}

describe("getApiHealth", () => {
  it("is ready when the readiness probe answers 2xx, with its checks and timings", async () => {
    answer(healthy, 200);

    const health = await getApiHealth();

    expect(health).toMatchObject({
      readiness: "ready",
      checksMs: 5.2,
      checks: { database: "healthy", self: "healthy" },
    });
    expect(health.roundTripMs).toBeGreaterThanOrEqual(0);
  });

  it("is not ready when the probe answers 503, and says which check failed", async () => {
    answer(
      JSON.stringify({ status: "unhealthy", checks: { database: "unhealthy", self: "healthy" } }),
      503,
    );

    await expect(getApiHealth()).resolves.toMatchObject({
      readiness: "not_ready",
      checks: { database: "unhealthy", self: "healthy" },
    });
  });

  it("keeps the readiness when the body is not the health writer's", async () => {
    answer("<html>bad gateway</html>", 502);

    await expect(getApiHealth()).resolves.toMatchObject({ readiness: "not_ready", checks: {} });
  });

  it("drops checks it does not recognise instead of trusting them", async () => {
    answer(JSON.stringify({ checks: { database: "on fire", self: "healthy", other: 3 } }), 200);

    const health = await getApiHealth();

    expect(health.checks).toEqual({ self: "healthy" });
    expect(health.checksMs).toBeUndefined();
  });

  it("is unreachable instead of throwing when the request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("fetch failed")));

    await expect(getApiHealth()).resolves.toEqual({ readiness: "unreachable", checks: {} });
  });

  it("asks the readiness probe uncached and with a timeout", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(healthy, { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await getApiHealth();

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("http://localhost:5080/health/ready");
    expect(init.cache).toBe("no-store");
    expect(init.signal).toBeInstanceOf(AbortSignal);
  });
});

describe("getWorkerHealth", () => {
  it("asks the worker's own readiness probe", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(healthy, { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await expect(getWorkerHealth()).resolves.toMatchObject({ readiness: "ready" });
    expect(fetchMock.mock.calls[0][0]).toBe("http://localhost:5081/health/ready");
  });
});
