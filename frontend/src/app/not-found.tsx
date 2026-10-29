import Link from "next/link";
import { buttonClasses } from "@/components/ui/button";
import { Container } from "@/components/ui/container";

export default function NotFound() {
  return (
    <Container className="py-16">
      <p className="text-sm font-medium text-muted">404</p>
      <h1 className="mt-2 text-2xl font-semibold tracking-tight text-balance">Page not found</h1>
      <p className="mt-2 max-w-prose text-pretty text-muted">
        Nothing lives at this address. Check the link, or start again from the dashboard.
      </p>
      <Link href="/" className={buttonClasses({ variant: "secondary", className: "mt-6" })}>
        Back to the dashboard
      </Link>
    </Container>
  );
}
