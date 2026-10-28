"use client";

import { Button } from "@/components/ui/button";
import { Container } from "@/components/ui/container";

/** Server errors reach the client with a generic message and a digest; the digest is what support needs. */
function digestOf(error: unknown): string | undefined {
  return typeof error === "object" &&
    error !== null &&
    "digest" in error &&
    typeof error.digest === "string"
    ? error.digest
    : undefined;
}

export default function Error({ error, retry }: { error: unknown; retry: () => void }) {
  const digest = digestOf(error);
  return (
    <Container className="py-16">
      <div role="alert">
        <h1 className="text-2xl font-semibold tracking-tight text-balance">Something went wrong</h1>
        <p className="mt-2 max-w-prose text-pretty text-muted">
          The page could not load. Try again; if it keeps failing, share the reference below.
        </p>
        {digest && (
          <p className="mt-3 text-sm text-muted">
            Reference: <code className="font-mono text-fg">{digest}</code>
          </p>
        )}
      </div>
      <Button className="mt-6" onClick={() => retry()}>
        Try again
      </Button>
    </Container>
  );
}
