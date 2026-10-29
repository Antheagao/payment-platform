using PaymentPlatform.Domain;
using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Domain.Payments;

namespace PaymentPlatform.Persistence;

/// <summary>
/// One row of a payment's audit trail: who or what moved it, when, from which status to which, and the
/// correlation id of the request or job that did it. Rows are only ever added, never changed or deleted.
/// </summary>
public sealed class PaymentEvent
{
    public const int ActorIdMaxLength = 100;
    public const int ReasonMaxLength = 64;

    private PaymentEvent(
        PaymentId paymentId,
        MerchantId merchantId,
        PaymentStatus? fromStatus,
        PaymentStatus toStatus,
        ActorKind actorKind,
        string actorId,
        DateTimeOffset occurredAt,
        string? reason,
        string? correlationId)
    {
        PaymentId = paymentId;
        MerchantId = merchantId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ActorKind = actorKind;
        ActorId = actorId;
        OccurredAt = occurredAt;
        Reason = reason;
        CorrelationId = correlationId;
    }

    /// <summary>Ascending in the order rows were written, so it breaks ties between events at the same instant.</summary>
    public long Id { get; private set; }

    public PaymentId PaymentId { get; }

    public MerchantId MerchantId { get; }

    public PaymentStatus? FromStatus { get; }

    public PaymentStatus ToStatus { get; }

    public ActorKind ActorKind { get; }

    public string ActorId { get; }

    public DateTimeOffset OccurredAt { get; }

    public string? Reason { get; }

    public string? CorrelationId { get; }

    public static PaymentEvent From(PaymentStatusChanged change, string? correlationId)
    {
        ArgumentNullException.ThrowIfNull(change);
        return new PaymentEvent(
            change.PaymentId,
            change.MerchantId,
            change.From,
            change.To,
            change.Actor.Kind,
            change.Actor.Id,
            change.OccurredAt,
            change.Reason,
            correlationId);
    }
}
