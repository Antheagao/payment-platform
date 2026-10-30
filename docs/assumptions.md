# Assumptions

Where the brief was ambiguous, these are the decisions we made and why.
Each is cheap to revisit; the ones that would be expensive to change have an ADR instead.

## Merchants and access

1. **Merchants are provisioned out of band.**
   There is no self-service sign-up; a seed script creates a demo merchant and its dashboard user.
   The brief asks for logging in, not onboarding, and onboarding (KYC, contracts) is a product of its own.
2. **A dashboard user belongs to exactly one merchant**, and every query is scoped to that merchant.
   Another merchant's payment answers `404`, never `403`, so ids cannot be probed.
3. **The same API serves the dashboard and merchant integrations.**
   Server-to-server API keys for merchant backends are a production addition, listed in the future improvements.

## Money and card data

4. **Amounts are integers in the currency's minor unit** (cents for USD, yen for JPY) with an ISO 4217 currency code.
   A payment has one currency; there is no currency conversion, and totals are never summed across currencies.
5. **Card data is test data, tokenized at the edge.**
   The create endpoint accepts a test card number, exchanges it at once for a token from a simulated vault, and keeps only the token, brand, last four digits, and expiry.
   The CVV is checked for shape and discarded with the number; it is never stored, and request bodies are never logged.
   The simulated vault accepts only published test card numbers, so a real card is refused rather than stored ([ADR 008](decisions/008-card-tokenization-at-the-edge.md)).
   In production, tokenization would move into hosted payment fields so that card numbers never reach the merchant or this API at all (PCI DSS SAQ A for the merchant).
6. **The acquirer is simulated.**
   A deterministic in-process adapter stands in for the card network: specific test card numbers decline, time out, or succeed, so every branch is reproducible in tests.

## The lifecycle

7. **Authorization happens during the create request.**
   The payment is stored as `Pending` before the acquirer is called and moves to `Authorized` or `Failed` after it answers, so a crash between the two, or an acquirer that does not answer, leaves a visible `Pending` row rather than a charge with no record.
   The worker's reconciliation job resolves those rows by asking the acquirer again, and fails a payment it never answers for after a day, about as long as the card networks keep an authorization.
8. **Capture is for the full authorized amount**; partial capture is out of scope.
9. **Refunds are full refunds**, allowed from `Captured` or `Settled`.
   Partial refunds need a refunded amount and a different state model, so they are a future improvement.
10. **Settlement is simulated.**
    A scheduled batch settles captured payments after a configurable delay, standing in for the acquirer's daily settlement file.

## The async workflow

11. **Webhooks are delivered at least once.**
    Each merchant has one webhook endpoint and a signing secret; receivers deduplicate by the event id.
    Events: `payment.authorized`, `payment.failed`, `payment.captured`, `payment.settled`, `payment.refunded`.
12. **Retries are bounded.**
    A delivery that keeps failing is tried 12 times with exponential backoff and full jitter, over at most about 3.4 hours, then dead-lettered for a person to inspect; a replay for operators is planned with the operator role (T26c).

## Operations

13. **One region, one database**, with tenancy by a `MerchantId` column on every merchant-owned row.
14. **Time is UTC** everywhere: storage, logs, the API, and the batch schedules.
15. **A login lasts 30 minutes** with no refresh; the dashboard sends the user back to the login page when it expires ([ADR 004](decisions/004-authentication-with-short-lived-jwts.md)).
