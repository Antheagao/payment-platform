using System.Collections.Frozen;

namespace PaymentPlatform.Domain.Monetary;

/// <summary>An ISO 4217 currency the platform accepts, with the number of digits in its minor unit.</summary>
public sealed record Currency
{
    // The minor-unit exponent decides what "1" means in AmountMinor: one cent for USD, one yen for JPY.
    private static readonly FrozenDictionary<string, Currency> Supported = new[]
    {
        new Currency("USD", 2),
        new Currency("EUR", 2),
        new Currency("GBP", 2),
        new Currency("CAD", 2),
        new Currency("AUD", 2),
        new Currency("JPY", 0),
    }.ToFrozenDictionary(currency => currency.Code, StringComparer.Ordinal);

    private Currency(string code, int minorUnitDigits)
    {
        Code = code;
        MinorUnitDigits = minorUnitDigits;
    }

    /// <summary>The three-letter upper-case code, such as <c>USD</c>.</summary>
    public string Code { get; }

    public int MinorUnitDigits { get; }

    public static IReadOnlyCollection<string> SupportedCodes => Supported.Keys;

    /// <summary>Parses a code case-insensitively; anything not on the allow-list is rejected.</summary>
    public static Result<Currency> Parse(string? code)
    {
        var normalized = code?.Trim().ToUpperInvariant();
        if (normalized is not null && Supported.TryGetValue(normalized, out var currency))
        {
            return currency;
        }

        return new DomainError("currency_not_supported", $"currency must be one of {string.Join(", ", Supported.Keys)}");
    }

    public override string ToString() => Code;
}
