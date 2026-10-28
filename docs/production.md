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
5. **Edge protection.**
   Front Door with a WAF policy in front of the API and the dashboard, rate limits at the edge as well as in the API, and the API reachable only from Front Door.
6. **Migrations in the pipeline** as an idempotent script under a migration identity, before the new build takes traffic; the readiness probe already keeps an instance out of rotation until its schema is current.

## Observability in production

- **Export:** the Azure Monitor OpenTelemetry distro sends the same traces, metrics, and logs to Application Insights that the Aspire dashboard shows locally ([ADR 005](decisions/005-observability-on-opentelemetry.md)); no code changes.
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
- A k6 load test against the composed stack (T31) sets the first real numbers and names the first bottleneck.

## Operations

- **Runbooks** for a dead-lettered delivery (inspect, fix the endpoint, replay), a payment stuck in `Pending` (reconciliation re-asks the acquirer), and a failed settlement batch.
- **Deployments:** blue-green slots on App Service, with the readiness probe gating the swap and a one-click swap back.
- **Dependencies:** Dependabot and CodeQL already run; production adds a container image scan before release.
