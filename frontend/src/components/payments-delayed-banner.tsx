import { getApiHealth } from "@/lib/api";

/**
 * What a merchant needs to know of the platform's health, and no more (ADR 011): one sentence when the API is not
 * ready, and nothing when it is. The services behind it, and how each is doing, are for the operator area.
 * Asked once per page view; render it inside `<Suspense>` with no fallback, so a healthy page never shows a gap.
 */
export async function PaymentsDelayedBanner() {
  const { readiness } = await getApiHealth();
  if (readiness === "ready") return null;
  return (
    <div role="status" className="mt-6 rounded-lg border border-border bg-warning-soft px-4 py-3">
      <p className="font-medium">Payments are delayed.</p>
      <p className="mt-1 text-sm text-pretty">
        The payments service is not answering right now. Nothing already taken is lost, and new
        payments and changes go through once it is back; there is no need to retry them.
      </p>
    </div>
  );
}
