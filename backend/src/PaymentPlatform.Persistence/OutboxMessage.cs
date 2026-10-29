using System.Diagnostics;
using System.Text.Json;
using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Domain.Payments;

namespace PaymentPlatform.Persistence;

/// <summary>
/// An event that a payment changed, written in the same transaction as the change (ADR 003), so there is never an
/// event without a change or a change without its event. The worker dispatches it into one webhook delivery per
/// endpoint the merchant has, restoring the request's correlation id and trace.
/// </summary>
public sealed class OutboxMessage
{
    public const int EventTypeMaxLength = 40;

    /// <summary>The length of a W3C <c>traceparent</c>: version, trace id, span id, and flags.</summary>
    public const int TraceParentMaxLength = 55;

    private static readonly JsonSerializerOptions PayloadJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private OutboxMessage(
        Guid eventId,
        MerchantId merchantId,
        PaymentId paymentId,
        string eventType,
        string payload,
        string? correlationId,
        string? traceParent,
        DateTimeOffset occurredAt)
    {
        EventId = eventId;
        MerchantId = merchantId;
        PaymentId = paymentId;
        EventType = eventType;
        Payload = payload;
        CorrelationId = correlationId;
        TraceParent = traceParent;
        OccurredAt = occurredAt;
    }

    // For EF Core, which sets every property from its column.
    private OutboxMessage()
    {
        EventType = null!;
        Payload = null!;
    }

    /// <summary>Ascending in commit order; the dispatcher takes the oldest first.</summary>
    public long Id { get; private set; }

    /// <summary>The event's stable id, sent with every delivery so a receiver can drop duplicates.</summary>
    public Guid EventId { get; }

    public MerchantId MerchantId { get; }

    public PaymentId PaymentId { get; }

    /// <summary><c>payment.authorized</c>, <c>payment.failed</c>, <c>payment.captured</c>, <c>payment.settled</c>, <c>payment.refunded</c>.</summary>
    public string EventType { get; }

    /// <summary>The webhook body, as JSON; never a card number, token, or CVV (ADR 008).</summary>
    public string Payload { get; }

    public string? CorrelationId { get; }

    /// <summary>The W3C <c>traceparent</c> of the span that made the change, which the worker continues.</summary>
    public string? TraceParent { get; }

    public DateTimeOffset OccurredAt { get; }

    /// <summary>When the dispatcher turned it into deliveries; until then it is in the queue.</summary>
    public DateTimeOffset? DispatchedAt { get; private set; }

    /// <summary>The event for a status change, or none: a payment's creation as <c>pending</c> is not news.</summary>
    public static OutboxMessage? For(Payment payment, PaymentStatusChanged change, string? correlationId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(change);
        if (change.From is null)
        {
            return null;
        }

        var eventId = Guid.CreateVersion7(now);
        var eventType = $"payment.{change.To.ToString().ToLowerInvariant()}";
        var payload = JsonSerializer.Serialize(
            new
            {
                Id = eventId,
                Type = eventType,
                CreatedAt = change.OccurredAt,
                Data = new
                {
                    PaymentId = payment.Id.Value,
                    Status = change.To.ToString().ToLowerInvariant(),
                    PreviousStatus = change.From.Value.ToString().ToLowerInvariant(),
                    payment.Amount.AmountMinor,
                    Currency = payment.Amount.Currency.Code,
                    payment.MerchantReference,
                    payment.FailureReason,
                },
            },
            PayloadJson);
        return new OutboxMessage(
            eventId, change.MerchantId, change.PaymentId, eventType, payload, correlationId, Activity.Current?.Id, change.OccurredAt);
    }

    public void MarkDispatched(DateTimeOffset now) => DispatchedAt = now;
}
