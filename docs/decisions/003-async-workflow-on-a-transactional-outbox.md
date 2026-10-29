# 003. The asynchronous workflow runs on a transactional outbox

- Status: accepted
- Date: 2026-10-28

## Context

The exercise requires at least one asynchronous workflow; we build two: webhook delivery to merchants and settlement of captured payments.
Both start from a payment state change, and the hardest failure in payments is a state change that is committed while its event is lost, or an event that goes out for a change that rolled back.
The workflow also has to show queue depth, processing lag, retries, and failures, and a trace that spans the API request and the background work.
The Azure design must map onto services the team already runs (Service Bus, Functions, Storage queues).

## Decision

- **Outbox table in SQL Server.** Every state change writes an `OutboxMessages` row (event type, payload, `traceparent`, correlation id, due time) in the same transaction as the change.
- **The worker claims rows** with `UPDATE TOP (@batch) ... WITH (UPDLOCK, READPAST, ROWLOCK) ... OUTPUT inserted.*`, setting a lease that expires after a few minutes, so several worker instances share the work without double-processing and a crashed worker's rows come back.
- **Webhook delivery is its own table.** The dispatcher turns each outbox message into one `WebhookDeliveries` row per subscribed endpoint, so one slow merchant never delays another, and each delivery has its own attempts, next-attempt time, last error, and status.
- **Retries:** exponential backoff with full jitter, 10 seconds doubling to 1 hour, at most 12 attempts (about a day); then the delivery is dead-lettered (`DeadLettered` status), counted, and left for an operator to replay.
  Each HTTP attempt has a 10-second timeout; `2xx` is success, `410 Gone` disables the endpoint, everything else retries.
- **Delivery is at least once.** Every event carries a stable id and an HMAC-SHA256 signature over the timestamp and raw body, so receivers can deduplicate and verify.
- **Settlement is a scheduled job** that calls a T-SQL stored procedure moving due `Captured` payments to `Settled` in set-based batches, writing their audit rows and outbox messages in the same transaction.
- **Metrics come from the tables:** depth is the count of due unclaimed rows, lag is the age of the oldest due row, and retries and dead letters are counted as they happen.
- **Trace continuity:** the worker restores the stored `traceparent` as the parent of its processing span, so the delivery joins the API request's trace.

## Rejected alternatives

- **Publish to a broker from the request** (Service Bus or RabbitMQ directly): without a distributed transaction the publish and the commit can disagree, which is exactly the failure the outbox prevents.
- **In-process `Channel<T>` or a background queue in the API:** lost on restart and invisible across instances.
- **Service Bus emulator or Azurite queues locally:** closer to production, but still needs the outbox for atomicity and adds containers a reviewer must start; the outbox relay to Service Bus is the production step, described below.
- **Hangfire or Quartz.NET:** capable schedulers, but they own their own schema and retry semantics, and the point here is to show the retry, lease, and dead-letter design rather than configure it.

## Consequences

- Polling adds up to a second of latency, which is acceptable for webhooks and settlement.
- The database carries the queue load; at higher volume the dispatcher becomes a relay that publishes outbox rows to Azure Service Bus and consumers become Service Bus-triggered Azure Functions, with the outbox still guaranteeing atomicity.
- Dead-lettered deliveries need an operator action (a replay endpoint or script), queued with T23.

## Amendments

- 2026-10-28: "request id" renamed to "correlation id"; see the amendment in [ADR 005](005-observability-on-opentelemetry.md).
- 2026-10-29: the outbox as built (T23a).
  A row holds the event id, type (`payment.<status>`), payload, correlation id, `traceparent`, and when the change happened; due times and attempts live on the deliveries, not the outbox.
  The dispatcher claims with `SELECT TOP (100) ... WITH (UPDLOCK, READPAST, ROWLOCK)` inside the transaction that writes the deliveries and marks the rows dispatched, rather than an `UPDATE ... OUTPUT` lease: dispatch does no network work and finishes in milliseconds, so the locks it holds until the commit are the lease, and a worker that dies mid-batch rolls back and frees its rows with no reclaim step.
  Deliveries, which wait on merchants' servers, keep the lease design (T23c).
  A payment's creation as `Pending` writes no event (nothing a merchant can act on has happened yet), and the demo seed's history writes none (it is history, not news).
