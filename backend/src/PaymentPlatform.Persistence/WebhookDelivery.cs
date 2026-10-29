using PaymentPlatform.Domain.Merchants;

namespace PaymentPlatform.Persistence;

public enum WebhookDeliveryStatus
{
    /// <summary>Due now or at <see cref="WebhookDelivery.NextAttemptAt"/>.</summary>
    Pending,

    /// <summary>The receiver answered <c>2xx</c>.</summary>
    Delivered,

    /// <summary>Every attempt failed, or the endpoint is gone; left for an operator to replay (ADR 003).</summary>
    DeadLettered,
}

/// <summary>
/// One event on its way to one endpoint, with its own attempts and schedule, so a slow or broken receiver never
/// delays another (ADR 003). The outbox message it came from holds the body; this row holds the progress.
/// </summary>
public sealed class WebhookDelivery
{
    public const int LastErrorMaxLength = 500;

    private WebhookDelivery(long outboxMessageId, Guid endpointId, MerchantId merchantId, DateTimeOffset now)
    {
        OutboxMessageId = outboxMessageId;
        EndpointId = endpointId;
        MerchantId = merchantId;
        Status = WebhookDeliveryStatus.Pending;
        NextAttemptAt = now;
        CreatedAt = now;
    }

    // For EF Core, which sets every property from its column.
    private WebhookDelivery()
    {
    }

    public long Id { get; private set; }

    public long OutboxMessageId { get; }

    public Guid EndpointId { get; }

    public MerchantId MerchantId { get; }

    public WebhookDeliveryStatus Status { get; private set; }

    /// <summary>Attempts made so far; the schedule and the dead letter count from it.</summary>
    public int Attempts { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    /// <summary>While set and in the future, a worker is attempting it; a crashed worker's lease simply runs out.</summary>
    public DateTimeOffset? LeasedUntil { get; private set; }

    public int? LastStatusCode { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public static WebhookDelivery For(OutboxMessage message, WebhookEndpoint endpoint, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(endpoint);
        return new WebhookDelivery(message.Id, endpoint.Id, message.MerchantId, now);
    }

    public void Delivered(int statusCode, DateTimeOffset now)
    {
        Attempts++;
        Status = WebhookDeliveryStatus.Delivered;
        LastStatusCode = statusCode;
        LastError = null;
        DeliveredAt = now;
        LeasedUntil = null;
    }

    /// <summary>Records a failed attempt and schedules the next one, or dead-letters when <paramref name="nextAttemptAt"/> is null.</summary>
    public void Failed(int? statusCode, string error, DateTimeOffset? nextAttemptAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        Attempts++;
        LastStatusCode = statusCode;
        LastError = error.Length <= LastErrorMaxLength ? error : error[..LastErrorMaxLength];
        LeasedUntil = null;
        if (nextAttemptAt is { } next)
        {
            NextAttemptAt = next;
        }
        else
        {
            Status = WebhookDeliveryStatus.DeadLettered;
        }
    }
}
