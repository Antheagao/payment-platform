using PaymentPlatform.Domain;
using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Domain.Payments;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.Application.Payments;

/// <summary>A payment as a merchant may see it: no card token, and its whole audit trail, oldest first.</summary>
public sealed record PaymentDetails(
    PaymentId Id,
    MerchantId MerchantId,
    PaymentStatus Status,
    long AmountMinor,
    string Currency,
    string? MerchantReference,
    CardBrand CardBrand,
    string CardLast4,
    int CardExpiryMonth,
    int CardExpiryYear,
    string? AuthorizationReference,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<PaymentHistoryEntry> History)
{
    public static PaymentDetails From(Payment payment, IEnumerable<PaymentEvent> events)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(events);
        return new PaymentDetails(
            payment.Id,
            payment.MerchantId,
            payment.Status,
            payment.Amount.AmountMinor,
            payment.Amount.Currency.Code,
            payment.MerchantReference,
            payment.Card.Brand,
            payment.Card.Last4,
            payment.Card.ExpiryMonth,
            payment.Card.ExpiryYear,
            payment.AuthorizationReference,
            payment.FailureReason,
            payment.CreatedAt,
            payment.UpdatedAt,
            events.OrderBy(row => row.OccurredAt).ThenBy(row => row.Id).Select(PaymentHistoryEntry.Of).ToList());
    }
}

/// <summary>One audit row: who or what moved the payment, when, from which status to which, and why.</summary>
public sealed record PaymentHistoryEntry(
    PaymentStatus? From,
    PaymentStatus To,
    ActorKind ActorKind,
    string ActorId,
    DateTimeOffset OccurredAt,
    string? Reason,
    string? CorrelationId)
{
    public static PaymentHistoryEntry Of(PaymentEvent row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new PaymentHistoryEntry(
            row.FromStatus, row.ToStatus, row.ActorKind, row.ActorId, row.OccurredAt, row.Reason, row.CorrelationId);
    }
}
