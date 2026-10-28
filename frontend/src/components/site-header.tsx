import Link from "next/link";
import { Suspense } from "react";
import { ApiStatus, ApiStatusFallback } from "@/components/api-status";
import { Container } from "@/components/ui/container";

export function SiteHeader() {
  return (
    <header className="sticky top-0 z-10 border-b border-border bg-surface/90 backdrop-blur">
      <Container className="flex h-16 items-center justify-between gap-4">
        <Link href="/" className="flex items-center gap-2.5 rounded-md text-base font-semibold">
          <span
            aria-hidden="true"
            className="grid size-8 place-items-center rounded-md bg-primary text-primary-fg"
          >
            <svg
              viewBox="0 0 24 24"
              className="size-4"
              fill="none"
              stroke="currentColor"
              strokeWidth="2.25"
            >
              <rect x="3" y="6" width="18" height="12" rx="2" />
              <path d="M3 10h18" />
            </svg>
          </span>
          Payment Platform
        </Link>
        <Suspense fallback={<ApiStatusFallback />}>
          <ApiStatus />
        </Suspense>
      </Container>
    </header>
  );
}
