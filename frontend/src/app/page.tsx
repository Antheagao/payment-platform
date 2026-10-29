import { Suspense } from "react";
import { LiveUpdates } from "@/components/live-updates";
import { PaymentLifecycle } from "@/components/payment-lifecycle";
import { ServiceStatus, ServiceStatusFallback } from "@/components/service-status";
import { Container } from "@/components/ui/container";

export default function Home() {
  return (
    <Container className="py-8 sm:py-12">
      <h1 className="text-2xl font-semibold tracking-tight text-balance sm:text-3xl">
        Merchant dashboard
      </h1>
      <p className="mt-2 max-w-3xl text-pretty text-muted">
        Search your payments, follow each one through its lifecycle, and see who changed what and
        when.
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

        <PaymentLifecycle />
      </div>
    </Container>
  );
}
