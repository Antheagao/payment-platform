# 008. Card tokenization at the edge, with a vault that holds only test cards

- Status: accepted
- Date: 2026-10-28

## Context

Creating a payment needs a card, and a card number is the most sensitive value the platform could hold: PCI DSS scope follows it into every log, trace, table, backup, and error message it touches.
The exercise asks for a PCI mindset even with stubbed card data, and the repository is public, so a real card number must never appear in it, even by accident.

## Decision

- **Tokenize on arrival.** `POST /api/v1/payments` validates the card's shape (digits, length, the Luhn checksum, a known brand, an expiry in the future, a CVV of the brand's length) and hands it to `ICardVault` before anything else happens.
  The vault returns a `PaymentCard`: its token, brand, last four digits, and expiry.
  Nothing after that call holds the number or the CVV; the acquirer is sent the token.
- **Keep only what a merchant may see.** `Payments` stores the token, brand, last four, and expiry; the CVV is checked and discarded, never stored.
  The API returns the brand, last four, and expiry, never the token.
- **The simulated vault accepts only published test card numbers** (`SimulatedCardVault.TestCards`, listed in `docs/demo.md`) and refuses every other valid number with `422 card_not_accepted`.
  So a real card typed into the demo is refused before it is stored, even as four digits.
  Its scenario cards come back as the tokens that make the simulated acquirer decline, report insufficient funds, or not answer.
- **The request object cannot print itself.** `CardInput.ToString()` masks both values, so a debugger, an exception message, or a careless log line shows `****`.
- **Proven by a test, not by review alone:** `CreatePaymentTests.No_card_number_or_cvv_is_stored_logged_or_returned` captures every log record at Trace and up, every column of every row the create wrote, and both a success and a validation response, and fails on the number, its grouped form, the CVV, or the token.

## Rejected alternatives

- **Store the number encrypted and tokenize later:** the database, its backups, and every process with the key enter PCI scope.
- **Accept any Luhn-valid number in the simulator:** easier to demo, but the first real card typed in would be stored as last four digits and reach the logs of whoever runs it.
- **Hosted payment fields now** (the card goes from the browser straight to the vault, and the API sees only a token): the production answer, and it would make the merchant SAQ A; out of reach without a real vault, so it is the documented next step.

## Consequences

- Swapping in a real vault is one adapter and its registration; the port already returns what the rest of the code needs.
- In production the card number should never reach the API: hosted fields post it to the vault, and `POST /payments` takes a token instead (production.md).
- Every later feature that touches a card (capture, refund, settlement) works from the token alone.
