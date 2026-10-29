using PaymentPlatform.Domain.Payments;

namespace PaymentPlatform.Application.Cards;

/// <summary>
/// A card as a merchant sends it, before the vault sees it. The number and CVV live only in this object, for
/// the length of one request: never logged, stored, or returned (docs/assumptions.md, assumption 5).
/// </summary>
public sealed record CardInput(string? Number, int? ExpMonth, int? ExpYear, string? Cvv)
{
    // Never print the number or the CVV, even in a debugger's watch window or an exception message.
    public override string ToString() => "CardInput { Number = ****, Cvv = *** }";

    /// <summary>The digits alone: merchants may send spaces or dashes between groups.</summary>
    public string NormalizedNumber => new((Number ?? string.Empty).Where(c => c is not (' ' or '-')).ToArray());

    /// <summary>
    /// Field errors as codes under <paramref name="prefix"/> (<c>card.number</c>, <c>card.exp_month</c>, ...):
    /// the shape of the number (digits, length, Luhn checksum, a known brand), an expiry in the future, and a
    /// CVV of the brand's length.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> Validate(string prefix, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        var number = NormalizedNumber;
        CardBrand? brand = null;
        if (number.Length == 0)
        {
            errors[$"{prefix}.number"] = ["card_number_required"];
        }
        else if (!number.All(char.IsAsciiDigit) || number.Length is < 12 or > 19 || !PassesLuhn(number))
        {
            errors[$"{prefix}.number"] = ["card_number_invalid"];
        }
        else if ((brand = BrandOf(number)) is null)
        {
            errors[$"{prefix}.number"] = ["card_brand_not_supported"];
        }

        if (ExpMonth is null)
        {
            errors[$"{prefix}.exp_month"] = ["card_exp_month_required"];
        }
        else if (ExpMonth is < 1 or > 12)
        {
            errors[$"{prefix}.exp_month"] = ["card_exp_month_invalid"];
        }

        if (ExpYear is null)
        {
            errors[$"{prefix}.exp_year"] = ["card_exp_year_required"];
        }
        else if (ExpYear < now.Year || ExpYear > now.Year + 20)
        {
            errors[$"{prefix}.exp_year"] = [ExpYear < now.Year ? "card_expired" : "card_exp_year_invalid"];
        }
        else if (ExpMonth is >= 1 and <= 12 && ExpYear == now.Year && ExpMonth < now.Month)
        {
            errors[$"{prefix}.exp_month"] = ["card_expired"];
        }

        var cvvLength = brand == CardBrand.Amex ? 4 : 3;
        if (string.IsNullOrEmpty(Cvv))
        {
            errors[$"{prefix}.cvv"] = ["card_cvv_required"];
        }
        else if (!Cvv.All(char.IsAsciiDigit) || (brand is null ? Cvv.Length is < 3 or > 4 : Cvv.Length != cvvLength))
        {
            errors[$"{prefix}.cvv"] = ["card_cvv_invalid"];
        }

        return errors;
    }

    /// <summary>The network a card number belongs to, from its leading digits and length.</summary>
    public static CardBrand? BrandOf(string number)
    {
        ArgumentNullException.ThrowIfNull(number);
        if (number.Length < 6)
        {
            return null;
        }

        var two = int.Parse(number.AsSpan(0, 2), provider: System.Globalization.CultureInfo.InvariantCulture);
        var three = int.Parse(number.AsSpan(0, 3), provider: System.Globalization.CultureInfo.InvariantCulture);
        var four = int.Parse(number.AsSpan(0, 4), provider: System.Globalization.CultureInfo.InvariantCulture);
        return number switch
        {
            _ when number[0] == '4' && number.Length is 13 or 16 or 19 => CardBrand.Visa,
            _ when (two is >= 51 and <= 55 || four is >= 2221 and <= 2720) && number.Length == 16 => CardBrand.Mastercard,
            _ when two is 34 or 37 && number.Length == 15 => CardBrand.Amex,
            _ when (four == 6011 || two == 65 || three is >= 644 and <= 649) && number.Length is >= 16 and <= 19 => CardBrand.Discover,
            _ => null,
        };
    }

    /// <summary>The Luhn (mod 10) checksum every card number carries, which catches a mistyped digit.</summary>
    public static bool PassesLuhn(string digits)
    {
        ArgumentNullException.ThrowIfNull(digits);
        var sum = 0;
        var doubleIt = false;
        for (var index = digits.Length - 1; index >= 0; index--)
        {
            var digit = digits[index] - '0';
            if (doubleIt)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            doubleIt = !doubleIt;
        }

        return sum % 10 == 0;
    }
}
