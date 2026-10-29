import { Badge, type Tone } from "@/components/ui/badge";
import { getApiHealth, type Readiness } from "@/lib/api";

const display: Record<Readiness, { label: string; tone: Tone }> = {
  ready: { label: "API ready", tone: "success" },
  not_ready: { label: "API not ready", tone: "warning" },
  unreachable: { label: "API unreachable", tone: "danger" },
};

/** The API's readiness, asked fresh on every request; render it inside `<Suspense>` with the fallback below. */
export async function ApiStatus() {
  const { label, tone } = display[(await getApiHealth()).readiness];
  return (
    <Badge tone={tone} role="status">
      {label}
    </Badge>
  );
}

/** Same footprint as the answer, so the header does not shift when it streams in. */
export function ApiStatusFallback() {
  return <Badge tone="neutral">Checking API</Badge>;
}
