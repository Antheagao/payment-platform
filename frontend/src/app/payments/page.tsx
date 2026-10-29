import type { Metadata } from "next";
import { redirect } from "next/navigation";
import { Suspense } from "react";
import { PaymentFiltersForm } from "@/components/payments/payment-filters-form";
import { PaymentsResults, PaymentsResultsFallback } from "@/components/payments/payments-results";
import { Container } from "@/components/ui/container";
import { pageHref, parseFilters } from "@/lib/payment-filters";

export const metadata: Metadata = {
  title: "Payments",
  description: "Your payments, newest first, with filters by status, date, amount, and reference.",
};

export default async function PaymentsPage({ searchParams }: PageProps<"/payments">) {
  const params = await searchParams;
  const parsed = parseFilters(params);
  const cursor = typeof params.cursor === "string" && params.cursor ? params.cursor : undefined;
  const returnTo = pageHref(parsed.filters, cursor);
  // A GET form sends its empty fields too; the address is the view's link, so it is kept to the filters set.
  if (
    Object.values(params).some(
      (value) => value === "" || (Array.isArray(value) && value.includes("")),
    )
  ) {
    redirect(returnTo);
  }

  return (
    <Container className="py-8 sm:py-12">
      <h1 className="text-2xl font-semibold tracking-tight text-balance sm:text-3xl">Payments</h1>
      <p className="mt-2 max-w-3xl text-pretty text-muted">
        Every payment you have taken, newest first. Filter by status, when it was created, its
        amount, or the start of your own reference.
      </p>
      <div className="mt-8">
        <PaymentFiltersForm filters={parsed.filters} errors={parsed.errors} />
      </div>
      <div className="mt-8">
        {/* Keyed by the view, so a new search shows the loading shape rather than the last results. */}
        <Suspense key={returnTo} fallback={<PaymentsResultsFallback />}>
          <PaymentsResults parsed={parsed} cursor={cursor} returnTo={returnTo} />
        </Suspense>
      </div>
    </Container>
  );
}
