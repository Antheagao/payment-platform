import { describe, expect, it } from "vitest";
import type { ServiceHealth } from "./api";
import { formatMs, pendingServiceRows, serviceRows } from "./service-status";

const ready: ServiceHealth = {
  readiness: "ready",
  roundTripMs: 6.4,
  checksMs: 5.2,
  checks: { database: "healthy", self: "healthy" },
};
const unreachable: ServiceHealth = { readiness: "unreachable", checks: {} };

describe("formatMs", () => {
  it("rounds to whole milliseconds with a thousands separator", () => {
    expect(formatMs(6.4)).toBe("6 ms");
    expect(formatMs(3004.2)).toBe("3,004 ms");
  });

  it("never reads as zero", () => {
    expect(formatMs(0.3)).toBe("<1 ms");
  });
});

describe("serviceRows", () => {
  it("lists the API, its database, and the worker, in that order", () => {
    expect(serviceRows(ready, ready).map((row) => row.name)).toEqual([
      "Payments API",
      "Database",
      "Background worker",
    ]);
  });

  it("shows a healthy stack with the measured times", () => {
    const [api, database, worker] = serviceRows(ready, { ...ready, roundTripMs: 22 });

    expect(api).toMatchObject({ label: "Ready", tone: "success", metric: { value: "6 ms" } });
    expect(database).toMatchObject({
      label: "Connected",
      tone: "success",
      metric: { value: "5 ms" },
    });
    expect(worker).toMatchObject({ label: "Ready", metric: { value: "22 ms" } });
  });

  it("blames the database, not the API, when only the database is down", () => {
    const [api, database] = serviceRows(
      {
        readiness: "not_ready",
        roundTripMs: 3004,
        checks: { database: "unhealthy", self: "healthy" },
      },
      ready,
    );

    expect(api).toMatchObject({
      label: "Not ready",
      tone: "warning",
      metric: { value: "3,004 ms" },
    });
    expect(database).toMatchObject({
      label: "Unavailable",
      tone: "danger",
      metric: { value: "No connection" },
    });
  });

  it("calls the database unknown, not down, when the API does not answer", () => {
    const [api, database] = serviceRows(unreachable, ready);

    expect(api).toMatchObject({
      label: "Unreachable",
      tone: "danger",
      metric: { value: "No answer" },
    });
    expect(database).toMatchObject({ label: "Unknown", tone: "neutral" });
  });

  it("reports the worker on its own", () => {
    const worker = serviceRows(ready, unreachable)[2];

    expect(worker).toMatchObject({ label: "Unreachable", tone: "danger" });
  });
});

describe("pendingServiceRows", () => {
  it("has the same tiles, checking, with no numbers yet", () => {
    expect(pendingServiceRows.map((row) => row.name)).toEqual(
      serviceRows(ready, ready).map((row) => row.name),
    );
    for (const row of pendingServiceRows) {
      expect(row).toMatchObject({ label: "Checking", tone: "neutral" });
      expect(row.metric).toBeUndefined();
    }
  });
});
