import type { Metadata } from "next";
import { Suspense } from "react";
import { LiveUpdates } from "@/components/live-updates";
import { DeadLetters, DeadLettersFallback } from "@/components/operator/dead-letters";
import { ServiceStatus, ServiceStatusFallback } from "@/components/service-status";
import { Container } from "@/components/ui/container";

export const metadata: Metadata = {
  title: "Operations",
  description: "The platform's services and the webhook deliveries that gave up.",
};

/**
 * The operator area's home (ADR 011): what the dashboard's server sees of each service, and every merchant's
 * dead-lettered webhook deliveries with their replay. Only a staff session opens it (src/proxy.ts).
 */
export default function OperatorPage() {
  return (
    <Container className="py-8 sm:py-12">
      <h1 className="text-2xl font-semibold tracking-tight text-balance sm:text-3xl">Operations</h1>
      <p className="mt-2 max-w-3xl text-pretty text-muted">
        The platform&apos;s services as the dashboard&apos;s server sees them, and the webhooks that
        gave up after every retry, for every merchant.
      </p>

      <div className="mt-10 space-y-12">
        <section aria-labelledby="status-heading">
          <div className="flex flex-wrap items-end justify-between gap-x-6 gap-y-3">
            <div>
              <h2 id="status-heading" className="text-base font-semibold">
                Service status
              </h2>
              <p className="mt-1 text-sm text-pretty text-muted">
                What the dashboard&apos;s server sees when it asks each service.
              </p>
            </div>
            <LiveUpdates />
          </div>
          <div className="mt-4">
            <Suspense fallback={<ServiceStatusFallback />}>
              <ServiceStatus />
            </Suspense>
          </div>
        </section>

        <section aria-labelledby="dead-letters-heading">
          <h2 id="dead-letters-heading" className="text-base font-semibold">
            Dead-lettered webhooks
          </h2>
          <p className="mt-1 max-w-3xl text-sm text-pretty text-muted">
            Deliveries that failed every attempt, newest first. Replay one once the merchant&apos;s
            receiver is fixed: it goes back on the queue with its retries from the start.
          </p>
          <div className="mt-4">
            <Suspense fallback={<DeadLettersFallback />}>
              <DeadLetters />
            </Suspense>
          </div>
        </section>
      </div>
    </Container>
  );
}
