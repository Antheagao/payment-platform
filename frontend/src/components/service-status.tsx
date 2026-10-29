import { Badge } from "@/components/ui/badge";
import { getApiHealth, getWorkerHealth } from "@/lib/api";
import { pendingServiceRows, serviceRows, type ServiceRow } from "@/lib/service-status";

/** Each service's readiness as the dashboard's server sees it; render inside `<Suspense>` with the fallback below. */
export async function ServiceStatus() {
  const [api, worker] = await Promise.all([getApiHealth(), getWorkerHealth()]);
  return <ServiceTiles rows={serviceRows(api, worker)} />;
}

/** The same tiles, checking, so the panel does not shift when the answers stream in. */
export function ServiceStatusFallback() {
  return <ServiceTiles rows={pendingServiceRows} />;
}

function ServiceTiles({ rows }: { rows: readonly ServiceRow[] }) {
  return (
    <ul className="grid gap-4 md:grid-cols-3">
      {rows.map((row) => (
        <ServiceTile key={row.name} row={row} />
      ))}
    </ul>
  );
}

function ServiceTile({ row: { name, role, label, tone, metric } }: { row: ServiceRow }) {
  return (
    <li className="flex flex-col rounded-lg border border-border bg-surface p-4 sm:p-6">
      <div className="flex items-center justify-between gap-3">
        <h3 className="truncate text-sm font-semibold">{name}</h3>
        <Badge tone={tone}>{label}</Badge>
      </div>
      <p className="mt-1 text-sm text-pretty text-muted">{role}</p>
      {metric ? (
        <p className="mt-6">
          <span className="block text-2xl font-semibold tracking-tight tabular-nums">
            {metric.value}
          </span>
          <span className="mt-1 block text-sm text-muted">{metric.label}</span>
        </p>
      ) : (
        // Same height as the metric: a 2xl line (32px) and a sm line (20px) with 4px between.
        <div aria-hidden="true" className="mt-6">
          <div className="h-8 w-20 rounded-md bg-surface-2 motion-safe:animate-pulse" />
          <div className="mt-1 h-5 w-40 rounded-md bg-surface-2 motion-safe:animate-pulse" />
        </div>
      )}
    </li>
  );
}
