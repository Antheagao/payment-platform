# Infrastructure

The platform's Azure shape as Bicep, matching the "Running in Azure" table in [docs/architecture.md](../docs/architecture.md).
It is validated on every push and never deployed: this repository is an exercise that runs locally, and a real deployment would need a subscription, a tenant, and someone to own the bill.

## What it describes

| Module | Resources |
| --- | --- |
| `monitoring.bicep` | Log Analytics workspace and workspace-based Application Insights, local auth disabled |
| `network.bicep` | A virtual network with an `app` subnet (delegated to App Service) and a `private-endpoints` subnet |
| `private-endpoint.bicep` | A reusable private endpoint plus its private DNS zone linked to the network |
| `key-vault.bicep` | Key Vault with RBAC authorization, purge protection, and no public network access |
| `sql.bicep` | Azure SQL with Microsoft Entra authentication only, TLS 1.2, no public access, and the serverless `payments` database |
| `service-bus.bicep` | Service Bus with shared keys disabled and the `payment-events` queue (duplicate detection, dead-lettering); private when Premium |
| `app-service.bicep` | One Linux plan with the API, worker, and dashboard sites: managed identities, HTTPS only, VNet integration, `/health/ready` probes; the worker has no inbound access |
| `role-assignments.bicep` | Key Vault Secrets User for the API and worker, and for the dashboard on its one secret, Service Bus sender and receiver for the worker, Monitoring Metrics Publisher for all three |

No secret appears anywhere: the apps reach SQL, Service Bus, Key Vault, and Application Insights with their managed identities, and the JWT signing key and the dashboard's key are Key Vault references.

## Validate

```sh
az bicep build --file infra/bicep/main.bicep --stdout > /dev/null
az bicep lint --file infra/bicep/main.bicep
az bicep build-params --file infra/bicep/main.bicepparam --stdout > /dev/null
```

CI runs the same three commands; `bicepconfig.json` makes the important linter rules errors.

## What a deployment would add

These steps are written down so the design is complete; none of them has been run.

1. `az deployment group what-if` and then `create` with `main.bicepparam`, with `SQL_ADMIN_GROUP_NAME` and `SQL_ADMIN_GROUP_OBJECT_ID` set to a real Entra group.
2. Two secrets in the vault: `jwt-signing-key`, 32 or more random bytes, base64; and `dashboard-key`, 32 or more random characters, which the API and the dashboard share (ADR 010).
3. Database users for the site identities: `infra/sql/grant-app-identities.sql`, run with `sqlcmd` by a member of the SQL admin group and given the `apiSiteName` and `workerSiteName` deployment outputs.
   The apps get read, write, and execute rights only; schema changes run from the pipeline as an idempotent migration script under a separate migration identity, so the running apps never hold DDL rights.
4. Front Door with a WAF policy in front of the API and the dashboard, and the API restricted to traffic from Front Door; the dashboard's `TRUSTED_PROXY_HOPS` then becomes `2`, because Front Door's address follows the person's in `X-Forwarded-For`.
