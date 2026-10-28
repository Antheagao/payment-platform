using PaymentPlatform.Domain.Monetary;
using PaymentPlatform.Domain.Payments;

namespace PaymentPlatform.Application.Acquiring;

/// <param name="CardToken">The vault token for the card; the acquirer never receives a card number from us.</param>
public sealed record AuthorizationRequest(PaymentId PaymentId, Money Amount, string CardToken);
