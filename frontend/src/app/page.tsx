export default function Home() {
  return (
    <main className="mx-auto flex w-full max-w-5xl flex-1 flex-col justify-center px-6 py-16">
      <h1 className="text-3xl font-semibold tracking-tight text-balance">Payment Platform</h1>
      <p className="mt-3 max-w-prose text-pretty">
        The merchant dashboard. Sign-in, payments, and payment details arrive with the payment
        slice.
      </p>
    </main>
  );
}
