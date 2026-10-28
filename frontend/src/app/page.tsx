import { PaymentLifecycle } from "@/components/payment-lifecycle";
import { Container } from "@/components/ui/container";

export default function Home() {
  return (
    <Container className="py-10 sm:py-14">
      <h1 className="text-3xl font-semibold tracking-tight text-balance">Merchant dashboard</h1>
      <p className="mt-3 max-w-prose text-lg text-pretty text-muted">
        Search your payments, follow each one through its lifecycle, and see who changed what and
        when.
      </p>
      <div className="mt-8">
        <PaymentLifecycle />
      </div>
    </Container>
  );
}
