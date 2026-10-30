# Future improvements

What we would build with more time, beyond the queued work in `notes/future.md`.
Each item names the gap it closes.

## Product

- **Partial captures and partial refunds**, with a refunded amount on the payment and a state model that allows several refunds up to the captured total; today a capture or a refund is always the whole amount.
- **Voids:** cancelling an authorization before capture, so reserved funds are released without a refund.
- **Authorization expiry:** an uncaptured authorization lapses after the network's window and is recorded as such; today it stays `Authorized` until captured.
- **Webhook endpoint management in the dashboard:** the API already sets, reads, and rotates the endpoint (`/webhook-endpoint`); the dashboard would add a page for it and the merchant's deliveries with their attempts; the replay of a dead letter stays staff work ([ADR 011](decisions/011-platform-staff-as-their-own-identity.md)).
- **Merchant API keys** with scopes and rotation, for server-to-server integrations; today a merchant's server signs in as a user, as the load test does.
- **Settlement reports per currency**, grouped by currency and never summed across them, from the batches `SettlePayments` already logs.

## Engineering

- **Staff sign-in through Microsoft Entra ID** in Azure, feeding the same `staff` policy, in place of the local staff passwords ([ADR 011](decisions/011-platform-staff-as-their-own-identity.md)), and roles within staff (read-only support, operators who may replay).
- **Refresh tokens** with rotation and reuse detection, so a session can outlive the 30-minute access token without a new password.
- **Property-based tests** (FsCheck) for the lifecycle and the money rules, beside the table-driven ones over all 36 status pairs.
- **A Python reconciliation export** that compares settled payments with the acquirer's settlement file and feeds a Power BI dataset; settlement exists, the acquirer's file does not, because the acquirer is simulated.
- **An Azure DevOps pipeline** mirroring the GitHub Actions gates, once there is an organization to run it in.
- **Chaos tests** in the composed stack: kill the worker mid-batch and the database mid-transaction, and assert that nothing is lost or doubled; the integration tests already stage a takeover mid-send and a lease that runs out, and this would do it to real processes.
- **The load test against Azure**, with the database at its production size, a realistic dataset, and the outbox relayed to Service Bus, which [load-test.md](load-test.md) names as the step that takes the queue off the database.
