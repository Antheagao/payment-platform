"use client";

import { buttonClasses } from "@/components/ui/button";
import { Container } from "@/components/ui/container";

/** The operator area could not read from the API; the service status above it says whether the API is up. */
export default function OperatorError({ retry }: { error: unknown; retry: () => void }) {
  return (
    <Container className="py-8 sm:py-12">
      <h1 className="text-2xl font-semibold tracking-tight text-balance sm:text-3xl">Operations</h1>
      <div role="alert" className="mt-8 rounded-lg border border-border bg-surface p-6">
        <p className="font-medium">The staff API did not answer.</p>
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
