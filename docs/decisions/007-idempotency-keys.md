# 007. Idempotency keys for create, capture, and refund

- Status: accepted
- Date: 2026-10-28

## Context

Clients retry: a timeout does not tell them whether the payment was created or the refund issued.
A retried create must not charge twice, and a retried capture or refund must not move money twice.
State checks alone are not enough for create, which has no prior state to check.

## Decision

- `POST /payments`, `POST /payments/{id}/capture`, and `POST /payments/{id}/refund` require an `Idempotency-Key` header: 1 to 255 printable ASCII characters, unique per merchant.
- Keys live in an `IdempotencyKeys` table with a unique index on `(MerchantId, Key)`, the SHA-256 of the method, path, and canonical body, the status (`InProgress` or `Completed`), and the stored status code and response body.
- **First request:** insert the key as `InProgress` before any work; the unique index is the lock, so two concurrent first requests cannot both proceed.
- **Repeat after completion with the same fingerprint:** replay the stored status and body with an `Idempotent-Replayed: true` header and do no work.
- **Repeat while in progress:** `409` with code `idempotency_request_in_progress`.
- **Same key, different fingerprint:** `422` with code `idempotency_key_reused`.
- **Failures:** a `5xx` releases the key so the client can retry the same request; a `4xx` is stored and replayed, because retrying an invalid request cannot succeed.
- Keys expire after 24 hours; a worker job deletes expired rows in batches.
- Payment state is a second line of defense: capturing a `Captured` payment with a new key is a `409` (`payment_not_capturable`), never a second capture.
- The acquirer call carries the payment id as the acquirer-side idempotency key, so a crash and reconciliation cannot authorize twice.

## Rejected alternatives

- **Deduplicate by a hash of the body alone:** two genuinely identical payments in a row are legitimate.
- **Store keys in a cache such as Redis:** faster, but a cache eviction would silently allow a double charge; the database is where the guarantee already lives.
- **Optional keys:** a client that forgets the header gets no protection; requiring it makes the safe path the only path.

## Consequences

- Every money-moving request costs one extra insert and one update.
- Stored responses are payment bodies, which hold no card number or CVV by construction (assumption 5 in [assumptions.md](../assumptions.md)).

## Amendments

- 2026-10-28, as built for create (T20b):
  - **The fingerprint never covers the card number or CVV.** A SHA-256 of a body that holds a card number is card data in itself: a number has few enough candidates once its first digits are known to brute-force. The fingerprint is a JSON array of the path, amount, currency, merchant reference, and the card's brand, last four, and expiry; two requests that differ only in the middle digits of the card would replay each other, which can never charge twice.
  - **Requests refused before any work are not stored.** A request that fails validation is answered before the key is claimed, and one the vault refuses (`card_not_accepted`) releases the key; nothing was created, the same request gets the same answer, and a corrected request may use the same key (as Stripe does). Every request that created a payment, authorized, declined, or left pending, stores its `201` and replays it.
  - **A claim whose request died is taken over.** The key names the payment it created, in the same transaction that first saves the payment; a claim older than 30 seconds (three times the request timeout) is abandoned, and the next retry takes it over and finishes that payment, authorizing a `Pending` one again under the same payment id, instead of creating a second. A failure releases the key the same way: deleted if nothing was created, otherwise marked abandoned. Takeovers race on a row version, so one retry wins.
  - **An expired key is a new key**; the worker's `delete-expired-idempotency-keys` job removes expired rows hourly, in batches of 1,000, and leaves a key still in use alone.
  - Codes as built: `400 idempotency_key_required` and `idempotency_key_invalid`, `409 idempotency_request_in_progress`, `422 idempotency_key_reused`.
- 2026-10-28, as built for capture and refund (T21): the fingerprint is the method, the path, and the payment id, because the request has no body; a key used on one operation and then another is `422 idempotency_key_reused`.
  Only a success is stored: a move the lifecycle refuses (`409 payment_not_capturable`), the acquirer's decline (`422 capture_declined` or `refund_declined`), and its silence (`503 acquirer_unavailable` with `Retry-After: 5`) leave the payment unchanged and release the key, so the same request may be tried again.
  Two moves racing with different keys both pass the state check; the payment's row version lets one save, and the other gets the lifecycle's `409`.
