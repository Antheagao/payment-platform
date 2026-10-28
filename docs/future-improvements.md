# Future improvements

What we would build with more time, beyond the queued work in `notes/future.md`.
Each item names the gap it closes.

## Product

- **Partial captures and partial refunds**, with a refunded amount on the payment and a state model that allows several refunds up to the captured total.
- **Voids:** cancelling an authorization before capture, so reserved funds are released without a refund.
- **Authorization expiry:** an uncaptured authorization lapses after the network's window and is recorded as such.
- **Webhook endpoint management** in the dashboard: register, rotate the signing secret, see deliveries, and replay a dead-lettered one.
- **Merchant API keys** with scopes and rotation, for server-to-server integrations.
- **Multi-currency settlement reports** grouped by currency, never summed across currencies.

## Engineering

- **Refresh tokens** with rotation and reuse detection, or Entra ID for staff.
- **OpenTelemetry in the dashboard** (`instrumentation.ts`), so a trace starts at the merchant's click rather than at the API.
- **A content security policy with nonces** for the dashboard's scripts.
- **Property-based tests** (FsCheck) for the lifecycle and money rules, and a concurrency test that fires the same idempotent request in parallel.
- **A Python reconciliation export** that compares settled payments with the acquirer's settlement file and feeds a Power BI dataset, once settlement exists to reconcile.
- **An Azure DevOps pipeline** mirroring the GitHub Actions gates, once there is an organization to run it in.
- **Chaos tests:** kill the worker mid-batch and the database mid-transaction in the composed stack, and assert that nothing is lost or doubled.
