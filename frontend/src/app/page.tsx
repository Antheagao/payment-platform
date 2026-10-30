import { Suspense } from "react";
import { PaymentLifecycle } from "@/components/payment-lifecycle";
import { PaymentsDelayedBanner } from "@/components/payments-delayed-banner";
import { buttonClasses } from "@/components/ui/button";
import { Container } from "@/components/ui/container";
import Link from "next/link";

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
      <Suspense fallback={null}>
        <PaymentsDelayedBanner />
      </Suspense>
      <Link href="/payments" className={buttonClasses({ className: "mt-6" })}>
        View payments
      </Link>

      <div className="mt-10">
        <PaymentLifecycle />
      </div>
    </Container>
  );
}
