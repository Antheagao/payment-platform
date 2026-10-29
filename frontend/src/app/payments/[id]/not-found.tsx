import Link from "next/link";
import { buttonClasses } from "@/components/ui/button";
import { Container } from "@/components/ui/container";

/** No payment of this merchant has that id: the same answer for one that is another merchant's (assumption 2). */
export default function PaymentNotFound() {
  return (
    <Container className="py-16">
      <h1 className="text-2xl font-semibold tracking-tight text-balance">Payment not found</h1>
      <p className="mt-2 max-w-prose text-pretty text-muted">
        None of your payments has that id. Check the link, or find the payment in the list.
      </p>
      <Link href="/payments" className={buttonClasses({ className: "mt-6" })}>
        Back to payments
      </Link>
    </Container>
  );
}
