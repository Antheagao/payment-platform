"use client";

import { buttonClasses } from "@/components/ui/button";
import { Container } from "@/components/ui/container";

/** The payments could not be read: the API was down or answered with an error. The filters stay in the URL. */
export default function PaymentsError({ retry }: { error: unknown; retry: () => void }) {
  return (
    <Container className="py-8 sm:py-12">
      <h1 className="text-2xl font-semibold tracking-tight text-balance sm:text-3xl">Payments</h1>
      <div role="alert" className="mt-8 rounded-lg border border-border bg-surface p-6">
        <p className="font-medium">The payments service did not answer.</p>
        <p className="mt-1 text-sm text-pretty text-muted">
          Nothing was changed. Try again in a moment.
        </p>
        <button type="button" onClick={retry} className={buttonClasses({ className: "mt-4" })}>
          Try again
        </button>
      </div>
    </Container>
  );
}
