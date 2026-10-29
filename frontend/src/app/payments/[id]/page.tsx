import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { PaymentDetail, PaymentDetailFallback } from "@/components/payments/payment-detail";
import { Container } from "@/components/ui/container";

export const metadata: Metadata = {
  title: "Payment",
  description: "One payment: its amount, card, status, and every change to it.",
};

const paymentId = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export default async function PaymentPage({ params }: PageProps<"/payments/[id]">) {
  const { id } = await params;
  if (!paymentId.test(id)) notFound();

  return (
    <Container className="py-8 sm:py-12">
      <Link href="/payments" className="text-sm font-medium text-muted hover:text-fg">
        ← Payments
      </Link>
      {/* The read is behind its own boundary, as the list's is: a redirect to sign in thrown at the page
          itself would be kept by the router with the page, and sent the browser back to sign in again
          once it had signed in and returned here. Keyed by the payment, so another one shows the shape. */}
      <Suspense key={id} fallback={<PaymentDetailFallback />}>
        <PaymentDetail id={id} />
      </Suspense>
    </Container>
  );
}
