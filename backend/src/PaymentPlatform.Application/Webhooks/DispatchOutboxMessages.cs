using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.Application.Webhooks;

/// <summary>
/// Turns outbox messages into webhook deliveries, one per enabled endpoint of the message's merchant (ADR 003),
/// and marks the messages dispatched, all in one transaction. A merchant with no endpoint has its messages
/// dispatched with no deliveries: the event happened, and nobody asked to hear about it.
/// </summary>
/// <remarks>
/// Several workers can run this at once: the claim takes row locks with <c>UPDLOCK, READPAST</c>, so each skips the
/// rows another holds, and the locks last until the commit. Dispatch does no network work, so there is no lease:
/// a worker that dies mid-batch rolls back and its rows are free again. Deliveries, which wait on merchants, use
/// leases instead (T23c).
/// </remarks>
public sealed partial class DispatchOutboxMessages(PaymentsDbContext db, TimeProvider timeProvider, ILogger<DispatchOutboxMessages> logger)
{
    public const int BatchSize = 100;

    /// <summary>Dispatches one batch, oldest first, and returns how many messages it took.</summary>
    public async Task<int> HandleAsync(CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            (db, timeProvider, logger),
            async (_, state, token) => await DispatchBatchAsync(state.db, state.timeProvider, state.logger, token),
            verifySucceeded: null,
            cancellationToken);
    }

    private static async Task<int> DispatchBatchAsync(
        PaymentsDbContext db, TimeProvider timeProvider, ILogger logger, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var messages = await db.OutboxMessages
            .FromSql($"""
                SELECT TOP ({BatchSize}) * FROM payments.OutboxMessages WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE DispatchedAt IS NULL
                ORDER BY Id
                """)
            .ToListAsync(cancellationToken);
        if (messages.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return 0;
        }

        var merchants = messages.Select(message => message.MerchantId).Distinct().ToList();
        var endpoints = await db.WebhookEndpoints
            .Where(endpoint => endpoint.Enabled && merchants.Contains(endpoint.MerchantId))
            .ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var deliveries = 0;
        foreach (var message in messages)
        {
            // The request that made the change is this span's parent, so the trace runs from it into the worker.
            ActivityContext.TryParse(message.TraceParent, null, out var parent);
            using var activity = ApplicationTelemetry.ActivitySource.StartActivity("outbox dispatch", ActivityKind.Consumer, parent);
            activity?.SetTag("messaging.message.id", message.EventId);
            activity?.SetTag("event.type", message.EventType);
            activity?.SetTag("payment.id", message.PaymentId.Value);

            // The names the API uses (ServiceDefaults' CorrelationIds, which this project may not reference).
            activity?.SetTag("correlation.id", message.CorrelationId);
            using var scope = logger.BeginScope(new[] { new KeyValuePair<string, object?>("CorrelationId", message.CorrelationId) });
            foreach (var endpoint in endpoints.Where(endpoint => endpoint.MerchantId == message.MerchantId))
            {
                db.WebhookDeliveries.Add(WebhookDelivery.For(message, endpoint, now));
                deliveries++;
            }

            message.MarkDispatched(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        LogDispatched(logger, messages.Count, deliveries);
        return messages.Count;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Dispatched {MessageCount} outbox messages into {DeliveryCount} webhook deliveries")]
    private static partial void LogDispatched(ILogger logger, int messageCount, int deliveryCount);
}
