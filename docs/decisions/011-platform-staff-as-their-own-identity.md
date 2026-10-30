# 011. Platform staff are an identity of their own, apart from merchant users

- Status: accepted
- Date: 2026-10-29

## Context

Two kinds of people use the platform.
A merchant's users see their own merchant's payments and nothing else (assumption 2).
The platform's operators look at the platform itself: whether the API, its database, and the worker are ready, and the webhook deliveries that gave up after every retry, of any merchant, which only an operator should put back on the queue (ADR 003).

Until now there was only the first kind, so the dashboard's home page showed every merchant the service status panel: the database, the worker, and how long each took to answer.
That is the platform's internals, which a merchant has no use for and should not see, and the dead-letter replay had nobody to belong to.

The owner weighed three models on 2026-10-29: an `operator` role on a merchant user, staff as their own identity, or no roles and the panel reworded in a merchant's terms, and chose the second.

## Decision

- **Staff are `StaffUser`s**, a type and a table of their own (`Domain/Staff/`, `StaffUsers`), with an email and a password hash checked by the same hasher, and no merchant id: their work crosses merchants by nature.
- **They sign in on their own route**, `POST /api/v1/staff/auth/login`, with the same limits per email and per person's address as merchants (ADR 010).
  Their token is signed with the same key and lifetime (ADR 004), names the staff user in `sub`, carries `role` `staff`, and has no `merchant_id`.
- **Two policies keep the kinds apart.**
  The default policy, on every merchant route, needs a user and a merchant id, so a staff token is `403` there; the `staff` policy, on every `/staff` route, needs the `staff` role, so a merchant's token is `403` there.
  Neither kind can reach the other's data by any route.
- **The dead-letter replay is staff work**: `GET /api/v1/staff/webhook-deliveries?status=dead_lettered` lists them, and `POST /api/v1/staff/webhook-deliveries/{id}/replay` puts one back on the queue with its attempts reset, logged with the staff user's id.
- **Locally, compose seeds one staff user** beside the demo merchant, as it seeds the merchant's login (`operator@example.com`, a public local-only password, refused in Production).
  **In Azure, staff sign in with Microsoft Entra ID** instead, through the same `staff` policy fed by Entra's tokens; that path is designed, not built.

## Rejected alternatives

- **An `operator` role on a merchant user:** the smallest change, but an operator would belong to one merchant while acting on all of them, and a mistake in one check would let a merchant's user act across tenants.
- **No roles, and the panel reworded for merchants:** it removes the internals from the merchant's view, but leaves the dead-letter replay with no one allowed to run it, and the operators with no view of their own.
- **Staff in Entra ID from the start:** the production answer, but it needs a tenant to run in, and the stack must start with no account (requirement 9).

## Consequences

- There are two sign-in paths to keep secure instead of one; they share the hasher, the limits, the token format, and the tests' shape, so a fix to one is a fix to both.
- A person who is both a merchant's user and an operator has two accounts and two sessions, which is how most payment platforms keep their staff tools apart.
- The dashboard gets an operator area of its own (T26d); until it does, staff use the API directly, as `docs/demo.md` shows.
