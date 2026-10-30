# Production considerations

What would change, and in what order, before this platform carried real money.
The Azure shape is already written down in [infra/](../infra/README.md); this page covers everything around it.

## Before the first real payment

1. **PCI scope reduction.**
   Card numbers move into hosted payment fields (an iframe served by a PCI DSS Level 1 vault), so neither the merchant's page nor this API ever sees one; the API receives only tokens, and the edge tokenization in [assumptions.md](assumptions.md) is deleted.
2. **A real acquirer adapter** behind `IAcquirer`, with its sandbox in CI, idempotency keys passed through, and a circuit breaker so an acquirer outage fails fast instead of holding threads.
3. **Identity.**
   Staff sign in with Microsoft Entra ID; merchants keep password login with multi-factor authentication, and merchant backends get scoped, rotatable API keys.
4. **Secrets and keys** live in Key Vault and reach the apps through managed identities and Key Vault references; the JWT signing key rotates with an overlap window during which both keys validate.
   Merchants' webhook signing secrets, stored as issued in the database today because signing needs them, move to Key Vault (or Always Encrypted columns), and a rotation keeps the old secret for a day, sending both signatures in `webhook-signature` so a receiver can switch without missing events.
5. **Edge protection.**
   Front Door with a WAF policy in front of the API and the dashboard, rate limits at the edge as well as in the API, and the API reachable only from Front Door; the dashboard's `TRUSTED_PROXY_HOPS` becomes 2, so the per-person login limit still reads the person's address ([ADR 010](decisions/010-client-address-from-the-dashboard.md)).
6. **Webhook egress** leaves through a NAT gateway with fixed addresses that merchants can allow-list, and the worker's subnet denies traffic to the platform's own address space, a second wall behind the SSRF rules; dead-lettered deliveries get a replay endpoint for operators.
7. **Migrations in the pipeline** as an idempotent script under a migration identity, run with `sqlcmd -I` (filtered indexes need `QUOTED_IDENTIFIER ON`), before the new build takes traffic; the readiness probe already keeps an instance out of rotation until its schema is current. The full script was applied twice to an empty database on SQL Server 2025 with no errors.
8. **Reconciliation of `Pending` payments** (T41): a job that asks the acquirer again for each payment left `Pending` past a deadline, by the payment id it already uses as the idempotency key, and moves it on, with an alert when one stays unresolved.
9. **Settlement on the acquirer's schedule**: `Settlement:Delay` becomes the acquirer's daily cut-off, the job reconciles each batch's totals against the acquirer's settlement file, and a mismatch pages a person.

## Observability in production

- **Export:** the Azure Monitor OpenTelemetry distro sends the .NET services' traces, metrics, and logs to Application Insights, the same signals the Aspire dashboard shows locally ([ADR 005](decisions/005-observability-on-opentelemetry.md)); the dashboard's traces (`@vercel/otel`) need an OTLP endpoint in front of it, a collector or Azure Monitor's OTLP ingestion ([infra/README.md](../infra/README.md)).
- **Sampling:** keep every trace with an error or a slow span, and sample the rest at a rate the budget allows.
- **Service level objectives**, each with an alert on its burn rate:
  - API availability 99.9% (non-5xx responses) and p99 latency under 500 ms for reads, 1.5 s for create.
  - Webhook delivery: 99% of events delivered within 5 minutes.
- **Alerts that page a person:** outbox oldest-pending age over 60 seconds, any dead-lettered delivery, settlement batch failure, a readiness probe failing on more than one instance, and the acquirer error rate over 2%.
- **Retention:** logs 30 days hot and a year archived; audit rows (`PaymentEvents`) are never deleted.

## Data

- **Backups:** Azure SQL point-in-time restore plus geo-redundant backups; the recovery objectives to agree with the business are a 5-minute RPO and a 1-hour RTO.
- **Retention and erasure:** payments and their audit rows are kept for the regulatory period; personal data that is not needed for a payment is not collected.
- **Reporting:** a read replica (or a nightly export) feeds Power BI, so analysts never query the primary.

## Scale

- The API is stateless and scales out behind the load balancer; the worker scales by instance count, because outbox and delivery rows are claimed with `UPDLOCK, READPAST`.
- Beyond one database's write capacity, the outbox relays to Service Bus and consumers move to Service Bus-triggered Azure Functions; search moves to a read replica or a search index fed by the same events.
- The [load test](load-test.md) on the composed stack: the API met its objectives up to 600 checkouts a second (about 2,000 requests a second) on one machine, and the webhooks fell behind first, at the single worker; a second worker bounded the backlog, and SQL Server's CPU, shared by everything, is the ceiling after that.
  So production starts with two worker instances and an alert on `webhook.deliveries.lag`, and the outbox's relay to Service Bus is the step that takes the queue off the database.

## Operations

- **Runbooks** for a dead-lettered delivery (inspect, fix the endpoint, replay), a payment stuck in `Pending` (the reconciliation job re-asks the acquirer, T41; until it exists, nothing moves such a payment), and a failed settlement batch.
- **Deployments:** blue-green slots on App Service, with the readiness probe gating the swap and a one-click swap back.
- **Dependencies:** Dependabot and CodeQL already run; production adds a container image scan before release.
