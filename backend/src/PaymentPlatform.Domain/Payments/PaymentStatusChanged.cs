using PaymentPlatform.Domain.Merchants;

namespace PaymentPlatform.Domain.Payments;

/// <summary>
/// A payment changed status. Persistence turns each one into an audit row and an outbox message in the
/// same transaction as the change (ADR 003).
/// </summary>
/// <param name="From">The previous status, or <see langword="null"/> when the payment was just created.</param>
/// <param name="Reason">Why, when the transition has one worth keeping, such as a decline code.</param>
public sealed record PaymentStatusChanged(
    PaymentId PaymentId,
    MerchantId MerchantId,
    PaymentStatus? From,
    PaymentStatus To,
    Actor Actor,
    DateTimeOffset OccurredAt,
    string? Reason);
