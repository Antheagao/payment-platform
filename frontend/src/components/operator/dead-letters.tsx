import { DeadLetterList } from "@/components/operator/dead-letter-list";
import { formatInstant } from "@/lib/payment-display";
import { listDeadLetters, StaffApiUnavailableError, type DeadLetter } from "@/lib/staff-api";

/**
 * Every merchant's dead-lettered webhook deliveries, read with the staff token for this request (ADR 011).
 * When the API cannot answer, this section says so in place rather than throwing: in an outage the operator
 * needs the service status beside it most, and a thrown error would take the whole page with it.
 */
export async function DeadLetters() {
  let deadLetters: DeadLetter[];
  try {
    deadLetters = await listDeadLetters("/operator");
  } catch (error) {
    if (!(error instanceof StaffApiUnavailableError)) throw error;
    return (
      <div role="alert" className="rounded-lg border border-border bg-surface p-6">
        <p className="font-medium">The dead letters could not be read.</p>
        <p className="mt-1 text-sm text-pretty text-muted">
          The API did not answer; the service status above says why. This list comes back with it.
        </p>
      </div>
    );
  }

  return (
    <DeadLetterList
      rows={deadLetters.map((deadLetter) => ({
        id: deadLetter.id,
        merchantId: deadLetter.merchantId,
        paymentId: deadLetter.paymentId,
        eventType: deadLetter.eventType,
        attempts: deadLetter.attempts,
        failure:
          deadLetter.lastError ??
          (deadLetter.lastStatusCode ? `HTTP ${deadLetter.lastStatusCode}` : "no answer"),
        endpointHost: deadLetter.endpointHost,
        createdAt: deadLetter.createdAt,
        createdAtLabel: formatInstant(deadLetter.createdAt),
      }))}
    />
  );
}

/** The list's shape while it is read. */
export function DeadLettersFallback() {
  return (
    <div aria-hidden="true" className="space-y-3">
      {Array.from({ length: 2 }, (_, index) => (
        <div key={index} className="h-28 rounded-lg bg-surface-2" />
      ))}
    </div>
  );
}
