import { headers } from "next/headers";
import Link from "next/link";
import { Suspense } from "react";
import { ApiStatus, ApiStatusFallback } from "@/components/api-status";
import { MainNav } from "@/components/main-nav";
import { StaffMenu } from "@/components/staff-menu";
import { Badge } from "@/components/ui/badge";
import { UserMenu, UserMenuFallback } from "@/components/user-menu";
import { Container } from "@/components/ui/container";
import { inOperatorArea, staffArea } from "@/lib/areas";

/**
 * The header of both areas (ADR 011): the merchant's, with its sections and the merchant's menu, and the
 * operator area's, marked as such, with the API's readiness and the staff menu. Merchants are never shown the
 * platform's internals; their home page says only whether payments are delayed.
 */
export async function SiteHeader() {
  const operator = inOperatorArea((await headers()).get("x-pathname") ?? "");
  return (
    <header className="sticky top-0 z-10 border-b border-border bg-surface/90 backdrop-blur">
      <Container className="flex h-16 items-center justify-between gap-4">
        <div className="flex min-w-0 items-center gap-6">
          <Link
            href={operator ? staffArea : "/"}
            className="flex shrink-0 items-center gap-2.5 rounded-md text-base font-semibold whitespace-nowrap"
          >
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
          {operator ? (
            // From sm up: at phone width the brand and the session fill the header, and the page's own
            // heading says where the operator is.
            <span className="hidden sm:inline-flex">
              <Badge tone="neutral">Operator</Badge>
            </span>
          ) : (
            <Suspense fallback={null}>
              <MainNav />
            </Suspense>
          )}
        </div>
        <div className="flex min-w-0 items-center gap-3 sm:gap-4">
          {/* Staff only, and from sm up: below it the header holds the brand and the session, and the
              operator page's panel shows the API. */}
          {operator && (
            <div className="hidden sm:block">
              <Suspense fallback={<ApiStatusFallback />}>
                <ApiStatus />
              </Suspense>
            </div>
          )}
          <Suspense fallback={<UserMenuFallback />}>
            {operator ? <StaffMenu /> : <UserMenu />}
          </Suspense>
        </div>
      </Container>
    </header>
  );
}
