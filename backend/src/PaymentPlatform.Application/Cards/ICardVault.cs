using PaymentPlatform.Domain;
using PaymentPlatform.Domain.Payments;

namespace PaymentPlatform.Application.Cards;

/// <summary>
/// Exchanges a card for a token at the edge, so nothing past the create request ever holds a card number
/// (docs/assumptions.md, assumption 5). In production this is a PCI-certified vault behind hosted payment
/// fields, and the card number never reaches the API at all.
/// </summary>
public interface ICardVault
{
    /// <summary>
    /// Tokenizes a card that passed <see cref="CardInput.Validate"/>; the number and the CVV go no further than
    /// this call. Fails with <c>card_not_accepted</c> for a card this vault will not hold.
    /// </summary>
    Result<PaymentCard> Tokenize(CardInput card);
}
