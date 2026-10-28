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
