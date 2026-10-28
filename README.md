# Payment Platform

A simplified payment processing platform: a C# / .NET backend API, a Next.js merchant dashboard, and an asynchronous settlement and webhook workflow.

Merchants log in, create payments, capture and refund them, and search their payment history.
Every state change is audited, every money-moving call is idempotent, and a single payment's journey can be traced from the API request through the background work.

The repository is being built in small, reviewable steps; the current state and the plan live in [`notes/present.md`](notes/present.md) and [`notes/future.md`](notes/future.md).
