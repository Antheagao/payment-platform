using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Domain.Payments;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.Application.Webhooks;

/// <summary>
/// Sends due webhook deliveries (ADR 003): claims a batch under a lease, signs and posts each one, and records the
/// outcome. <c>2xx</c> is delivered; <c>410 Gone</c> disables the endpoint and dead-letters; anything else, or no
/// answer, is tried again on <see cref="WebhookRetrySchedule"/> until it is dead-lettered. A disabled endpoint's
/// deliveries are dead-lettered without being sent, for an operator to replay.
/// </summary>
/// <remarks>
/// Delivery is at least once. The claim sets <c>LeasedUntil</c> with <c>UPDLOCK, READPAST</c>, so workers share the
/// queue; a worker that dies leaves its lease to run out, and the delivery is taken again. Each outcome is saved only
/// while the lease stands (the lease is a concurrency token), so a slow worker never overwrites a later one; the
/// receiver may then see an event twice and drops it by its <c>webhook-id</c>.
/// </remarks>
public sealed partial class DeliverWebhooks(
    PaymentsDbContext db,
    IServiceScopeFactory scopes,
    IWebhookSender sender,
    QueueMetrics metrics,
    TimeProvider timeProvider,
    Random random,
    ILogger<DeliverWebhooks> logger)
{
    public const int BatchSize = 50;

    /// <summary>Longer than an attempt can take (the 10 s timeout) with room to save the outcome.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(1);

    // One slow receiver must not hold up the rest of the batch.
    private const int Parallelism = 16;

    /// <summary>Claims and delivers one batch, and returns how many deliveries it took.</summary>
    public async Task<int> HandleAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var leasedUntil = now + Lease;
        var claimed = await db.Database.SqlQuery<long>($"""
            WITH due AS (
                SELECT TOP ({BatchSize}) Id, LeasedUntil FROM payments.WebhookDeliveries WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE Status = 'Pending' AND NextAttemptAt <= {now} AND (LeasedUntil IS NULL OR LeasedUntil <= {now})
                ORDER BY NextAttemptAt)
            UPDATE due SET LeasedUntil = {leasedUntil}
            OUTPUT inserted.Id AS [Value]
            """).ToListAsync(cancellationToken);
        if (claimed.Count == 0)
        {
            return 0;
        }

        var work = await (
                from delivery in db.WebhookDeliveries.AsNoTracking()
                join message in db.OutboxMessages on delivery.OutboxMessageId equals message.Id
                join endpoint in db.WebhookEndpoints on delivery.EndpointId equals endpoint.Id
                where claimed.Contains(delivery.Id)
                select new Work(
                    delivery.Id,
                    delivery.Attempts,
                    message.EventId,
                    message.EventType,
                    message.Payload,
                    message.PaymentId,
                    message.MerchantId,
                    message.CorrelationId,
                    message.TraceParent,
                    endpoint.Url,
                    endpoint.Secret,
                    endpoint.Enabled))
            .ToListAsync(cancellationToken);

        await Parallel.ForEachAsync(
            work,
            new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = cancellationToken },
            async (item, token) => await DeliverAsync(item, leasedUntil, token));
        return claimed.Count;
    }

    private async Task DeliverAsync(Work item, DateTimeOffset leasedUntil, CancellationToken cancellationToken)
    {
        // The event's own trace, from the request that caused it, continues here, as it did for the dispatch.
        ActivityContext.TryParse(item.TraceParent, null, out var parent);
        using var activity = ApplicationTelemetry.ActivitySource.StartActivity("webhook deliver", ActivityKind.Producer, parent);
        activity?.SetTag("messaging.message.id", item.EventId);
        activity?.SetTag("event.type", item.EventType);
        activity?.SetTag("payment.id", item.PaymentId.Value);
        activity?.SetTag("correlation.id", item.CorrelationId);
        activity?.SetTag("webhook.attempt", item.Attempts + 1);
        using var scope = logger.BeginScope(new[] { new KeyValuePair<string, object?>("CorrelationId", item.CorrelationId) });

        var url = new Uri(item.Url);
        var attempt = item.Enabled
            ? await SendAsync(item, url, cancellationToken)
            : new WebhookAttempt(null, "the endpoint is disabled");
        activity?.SetTag("http.response.status_code", attempt.StatusCode);
        if (!attempt.Delivered)
        {
            activity?.SetStatus(ActivityStatusCode.Error, attempt.Error);
        }

        await RecordAsync(item, url, attempt, leasedUntil, cancellationToken);
    }

    private async Task<WebhookAttempt> SendAsync(Work item, Uri url, CancellationToken cancellationToken)
    {
        var key = WebhookSecrets.KeyOf(item.Secret);
        if (key is null)
        {
            return new WebhookAttempt(null, "the endpoint's secret is not in the whsec_ form");
        }

        var id = item.EventId.ToString();
        var timestamp = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var request = new WebhookRequest(url, id, timestamp, item.Payload, WebhookSignature.Sign(key, id, timestamp, item.Payload), item.CorrelationId);
        return await sender.SendAsync(request, cancellationToken);
    }

    /// <summary>Saves the outcome in a context of its own (the batch runs in parallel), only while this worker's lease stands.</summary>
    private async Task RecordAsync(Work item, Uri url, WebhookAttempt attempt, DateTimeOffset leasedUntil, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var delivery = await context.WebhookDeliveries.SingleAsync(row => row.Id == item.DeliveryId, cancellationToken);
        if (delivery.LeasedUntil != leasedUntil || delivery.Status != WebhookDeliveryStatus.Pending)
        {
            LogLeaseLost(logger, item.EventId, item.DeliveryId);
            return;
        }

        var now = timeProvider.GetUtcNow();
        var gone = attempt.StatusCode == 410;
        if (attempt.Delivered)
        {
            delivery.Delivered(attempt.StatusCode!.Value, now);
        }
        else
        {
            var next = gone || !item.Enabled ? null : WebhookRetrySchedule.NextAttemptAt(delivery.Attempts + 1, now, random.NextDouble());
            delivery.Failed(attempt.StatusCode, attempt.Error ?? "failed", next);
        }

        if (gone)
        {
            var endpoint = await context.WebhookEndpoints.SingleAsync(candidate => candidate.MerchantId == item.MerchantId, cancellationToken);
            endpoint.Disable(now);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogLeaseLost(logger, item.EventId, item.DeliveryId);
            return;
        }

        metrics.RecordAttempt(delivery.Status switch
        {
            WebhookDeliveryStatus.Delivered => "delivered",
            WebhookDeliveryStatus.DeadLettered => "dead_lettered",
            _ => "retried",
        });

        // The host only, as everywhere: the URL's path or query can hold a merchant's own token.
        var host = url.IdnHost;
        switch (delivery.Status)
        {
            case WebhookDeliveryStatus.Delivered:
                LogDelivered(logger, item.EventId, item.EventType, host, delivery.LastStatusCode, delivery.Attempts);
                break;
            // Retries ran out: an operator has to replay it (ADR 005: Error needs a person). A dead letter the merchant
            // caused, with a 410 or a disabled endpoint, is theirs to act on.
            case WebhookDeliveryStatus.DeadLettered when gone || !item.Enabled:
                LogDeadLetteredByEndpoint(logger, item.EventId, item.EventType, host, delivery.LastError);
                break;
            case WebhookDeliveryStatus.DeadLettered:
                LogDeadLettered(logger, item.EventId, item.EventType, host, delivery.Attempts, delivery.LastError);
                break;
            default:
                LogRetrying(logger, item.EventId, item.EventType, host, delivery.Attempts, delivery.LastError, delivery.NextAttemptAt);
                break;
        }

        if (gone)
        {
            LogEndpointGone(logger, item.MerchantId, host);
        }
    }

    private sealed record Work(
        long DeliveryId,
        int Attempts,
        Guid EventId,
        string EventType,
        string Payload,
        PaymentId PaymentId,
        MerchantId MerchantId,
        string? CorrelationId,
        string? TraceParent,
        string Url,
        string Secret,
        bool Enabled);

    [LoggerMessage(Level = LogLevel.Information, Message = "Webhook {EventId} ({EventType}) delivered to {Host}: HTTP {StatusCode} on attempt {Attempts}")]
    private static partial void LogDelivered(ILogger logger, Guid eventId, string eventType, string host, int? statusCode, int attempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook {EventId} ({EventType}) to {Host} failed attempt {Attempts}: {Error}; next attempt at {NextAttemptAt:O}")]
    private static partial void LogRetrying(ILogger logger, Guid eventId, string eventType, string host, int attempts, string? error, DateTimeOffset nextAttemptAt);

    [LoggerMessage(Level = LogLevel.Error, Message = "Webhook {EventId} ({EventType}) to {Host} dead-lettered after {Attempts} attempts, waiting for a replay: {Error}")]
    private static partial void LogDeadLettered(ILogger logger, Guid eventId, string eventType, string host, int attempts, string? error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook {EventId} ({EventType}) to {Host} dead-lettered by its endpoint: {Error}")]
    private static partial void LogDeadLetteredByEndpoint(ILogger logger, Guid eventId, string eventType, string host, string? error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook endpoint of merchant {MerchantId} at {Host} answered 410 Gone and is disabled")]
    private static partial void LogEndpointGone(ILogger logger, MerchantId merchantId, string host);

    [LoggerMessage(Level = LogLevel.Information, Message = "Webhook {EventId}: the lease on delivery {DeliveryId} ran out before the outcome was saved; another worker has it")]
    private static partial void LogLeaseLost(ILogger logger, Guid eventId, long deliveryId);
}
