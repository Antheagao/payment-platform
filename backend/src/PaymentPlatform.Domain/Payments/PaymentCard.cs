namespace PaymentPlatform.Domain.Payments;

/// <summary>The card networks the vault recognizes. Persisted and serialized by name.</summary>
public enum CardBrand
{
    Visa,
    Mastercard,
    Amex,
    Discover,
}

/// <summary>
/// What the platform keeps about the card a payment was made with: the vault's token and what a merchant may
/// see. Never a card number or a CVV (docs/assumptions.md, assumption 5).
/// </summary>
public sealed record PaymentCard
{
    public PaymentCard(string token, CardBrand brand, string last4, int expiryMonth, int expiryYear)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(last4);
        if (last4.Length != 4 || !last4.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("last4 must be four digits", nameof(last4));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(expiryMonth, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(expiryMonth, 12);
        ArgumentOutOfRangeException.ThrowIfLessThan(expiryYear, 2000);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(expiryYear, 2100);
        Token = token;
        Brand = brand;
        Last4 = last4;
        ExpiryMonth = expiryMonth;
        ExpiryYear = expiryYear;
    }

    /// <summary>The vault's reference to the card; only the acquirer can turn it back into a card.</summary>
    public string Token { get; }

    public CardBrand Brand { get; }

    public string Last4 { get; }

    public int ExpiryMonth { get; }

    public int ExpiryYear { get; }
}
