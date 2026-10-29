import type { Metadata } from "next";
import { LoginForm } from "@/components/login-form";
import { Container } from "@/components/ui/container";
import { safeRedirectTarget } from "@/lib/redirect-target";

export const metadata: Metadata = {
  title: "Sign in",
  description: "Sign in to the merchant dashboard.",
};

function first(value: string | string[] | undefined): string | undefined {
  return Array.isArray(value) ? value[0] : value;
}

export default async function LoginPage({ searchParams }: PageProps<"/login">) {
  const params = await searchParams;
  const next = safeRedirectTarget(first(params.next));
  const expired = first(params.reason) === "expired";
  // The seeded demo login is public on purpose (DemoSeed), and shown only where compose turns it on.
  const showDemoLogin = process.env.DEMO_LOGIN_HINT === "true";

  return (
    <Container className="py-12 sm:py-16">
      <div className="mx-auto w-full max-w-sm rounded-lg border border-border bg-surface p-6 sm:p-8">
        <h1 className="text-2xl font-semibold tracking-tight text-balance">Sign in</h1>
        <p className="mt-2 text-sm text-pretty text-muted">
          Use the email and password of your merchant account.
        </p>
        {expired && (
          <p role="status" className="mt-4 rounded-md bg-warning-soft px-3 py-2 text-sm text-fg">
            Your session ended. Sign in again to continue.
          </p>
        )}
        <LoginForm next={next} />
        {showDemoLogin && (
          <p className="mt-6 border-t border-border pt-4 text-sm text-muted">
            Local demo: <span className="font-medium text-fg">demo@example.com</span> with{" "}
            <span className="font-mono text-fg">LocalOnly_Demo_2026!</span>
          </p>
        )}
      </div>
    </Container>
  );
}
