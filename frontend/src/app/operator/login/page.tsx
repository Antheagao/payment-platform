import type { Metadata } from "next";
import { staffSignInAction } from "@/app/operator/login/actions";
import { LoginForm } from "@/components/login-form";
import { Container } from "@/components/ui/container";
import { safeRedirectTarget } from "@/lib/redirect-target";

export const metadata: Metadata = {
  title: "Operator sign-in",
  description: "Sign in to the platform's operator area.",
};

function first(value: string | string[] | undefined): string | undefined {
  return Array.isArray(value) ? value[0] : value;
}

/** The staff sign-in (ADR 011): the merchant login's form, against the staff route. */
export default async function OperatorLoginPage({ searchParams }: PageProps<"/operator/login">) {
  const params = await searchParams;
  const next = safeRedirectTarget(first(params.next));
  const expired = first(params.reason) === "expired";
  // The seeded operator login is public on purpose (DemoSeed), and shown only where compose turns it on.
  const showDemoLogin = process.env.DEMO_LOGIN_HINT === "true";

  return (
    <Container className="py-12 sm:py-16">
      <div className="mx-auto w-full max-w-sm rounded-lg border border-border bg-surface p-6 sm:p-8">
        <h1 className="text-2xl font-semibold tracking-tight text-balance">Operator sign-in</h1>
        <p className="mt-2 text-sm text-pretty text-muted">
          For the platform&apos;s staff. Merchants use{" "}
          {/* A full page load, not a client navigation: the header belongs to the area and the session, and the root
              layout that holds it is kept across client navigations (ADR 011). */}
          <a href="/login" className="font-medium text-fg underline underline-offset-4">
            the merchant sign-in
          </a>
          .
        </p>
        {expired && (
          <p role="status" className="mt-4 rounded-md bg-warning-soft px-3 py-2 text-sm text-fg">
            Your session ended. Sign in again to continue.
          </p>
        )}
        <LoginForm next={next} action={staffSignInAction} />
        {showDemoLogin && (
          <p className="mt-6 border-t border-border pt-4 text-sm text-muted">
            Local demo: <span className="font-medium text-fg">operator@example.com</span> with{" "}
            <span className="font-mono text-fg">LocalOnly_Operator_2026!</span>
          </p>
        )}
      </div>
    </Container>
  );
}
