using System.Collections.Frozen;
using System.Security.Cryptography;
using PaymentPlatform.Application.Acquiring;
using PaymentPlatform.Domain;
using PaymentPlatform.Domain.Payments;

namespace PaymentPlatform.Application.Cards;

/// <summary>
/// A stand-in vault that accepts only published test card numbers, so a real card can never be stored here,
/// not even as its last four digits. The scenario cards come back as the tokens that make the
/// <see cref="SimulatedAcquirer"/> decline or not answer; every other test card gets a fresh random token.
/// </summary>
public sealed class SimulatedCardVault : ICardVault
{
    public static readonly DomainError NotAccepted = new(
        "card_not_accepted", "the simulated vault accepts only test card numbers; docs/demo.md lists them");

    /// <summary>The cards this vault accepts, with what each one does; docs/demo.md shows the same list.</summary>
    public static readonly FrozenDictionary<string, string?> TestCards = new Dictionary<string, string?>
    {
        ["4242424242424242"] = null,
        ["5555555555554444"] = null,
        ["378282246310005"] = null,
        ["6011111111111117"] = null,
        ["4000000000000002"] = SimulatedCardTokens.CardDeclined,
        ["4000000000009995"] = SimulatedCardTokens.InsufficientFunds,
        ["4000000000000119"] = SimulatedCardTokens.AcquirerUnavailable,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public Result<PaymentCard> Tokenize(CardInput card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var number = card.NormalizedNumber;
        if (!TestCards.TryGetValue(number, out var scenarioToken))
        {
            return NotAccepted;
        }

        var brand = CardInput.BrandOf(number)
            ?? throw new InvalidOperationException("a test card without a brand; the list above is wrong");
        var token = scenarioToken ?? $"tok_sim_{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12))}";
        return new PaymentCard(token, brand, number[^4..], card.ExpMonth!.Value, card.ExpYear!.Value);
    }
}
