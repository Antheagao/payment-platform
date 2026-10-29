import type { Tone } from "@/components/ui/badge";
import type { CheckStatus, Readiness, ServiceHealth } from "@/lib/api";

/**
 * One tile of the service status panel: who, how it is, and one measured number with what it means.
 * A tile without a metric is still waiting for its answer.
 */
export type ServiceRow = {
  name: string;
  role: string;
  label: string;
  tone: Tone;
  metric?: { value: string; label: string };
};

const readinessDisplay: Record<Readiness, { label: string; tone: Tone }> = {
  ready: { label: "Ready", tone: "success" },
  not_ready: { label: "Not ready", tone: "warning" },
  unreachable: { label: "Unreachable", tone: "danger" },
};

const databaseDisplay: Record<CheckStatus, { label: string; tone: Tone }> = {
  healthy: { label: "Connected", tone: "success" },
  degraded: { label: "Degraded", tone: "warning" },
  unhealthy: { label: "Unavailable", tone: "danger" },
};

const milliseconds = new Intl.NumberFormat("en-US", { maximumFractionDigits: 0 });

/** A duration as the panel shows it: whole milliseconds, and never a misleading "0 ms". */
export function formatMs(ms: number): string {
  return ms < 1 ? "<1 ms" : `${milliseconds.format(ms)} ms`;
}

function serviceRow(name: string, role: string, health: ServiceHealth): ServiceRow {
  return {
    name,
    role,
    ...readinessDisplay[health.readiness],
    metric:
      health.roundTripMs === undefined
        ? { value: "No answer", label: "Refused, or nothing within 5 seconds" }
        : { value: formatMs(health.roundTripMs), label: "Round trip from the dashboard" },
  };
}

/**
 * The database as the API sees it. The dashboard never talks to the database itself, so when the
 * API does not answer, the database's state is unknown rather than down.
 */
function databaseRow(api: ServiceHealth): ServiceRow {
  const name = "Database";
  const role = "SQL Server, as the API reaches it";
  const check = api.checks.database;
  if (check === undefined) {
    return {
      name,
      role,
      label: "Unknown",
      tone: "neutral",
      metric: { value: "Not reported", label: "The API did not answer" },
    };
  }
  return {
    name,
    role,
    ...databaseDisplay[check],
    metric:
      check === "unhealthy"
        ? { value: "No connection", label: "The API cannot reach it" }
        : {
            value: api.checksMs === undefined ? "Not timed" : formatMs(api.checksMs),
            label: "Connection and migration check",
          },
  };
}

/** The three tiles, in the order a request travels: the API, its database, then the worker behind it. */
export function serviceRows(api: ServiceHealth, worker: ServiceHealth): ServiceRow[] {
  return [
    serviceRow("Payments API", "Serves merchants and this dashboard", api),
    databaseRow(api),
    serviceRow("Background worker", "Runs the jobs behind each payment", worker),
  ];
}

const unknown: ServiceHealth = { readiness: "unreachable", checks: {} };

/** The same three tiles while the answers stream in, so the panel keeps its size. */
export const pendingServiceRows: readonly ServiceRow[] = serviceRows(unknown, unknown).map(
  ({ name, role }) => ({ name, role, label: "Checking", tone: "neutral" }),
);
