namespace PaymentPlatform.Domain.Monetary;

/// <summary>
/// An amount in a currency's minor unit (cents for USD, yen for JPY). Integers only: money is never a
/// float, and two amounts in different currencies are never added or compared.
/// </summary>
public readonly record struct Money
{
    /// <summary>
    /// The largest amount one payment may carry, in minor units (999,999.99 in a two-digit currency).
    /// A business limit, not a technical one; raising it is a product decision.
    /// </summary>
    public const long MaxAmountMinor = 99_999_999;

    private Money(long amountMinor, Currency currency)
    {
        AmountMinor = amountMinor;
        Currency = currency;
    }

    public long AmountMinor { get; }

    public Currency Currency { get; }

    public static Result<Money> Create(long amountMinor, string? currencyCode)
    {
        if (amountMinor <= 0)
        {
            return new DomainError("amount_must_be_positive", "amount must be greater than zero");
        }

        if (amountMinor > MaxAmountMinor)
        {
            return new DomainError("amount_too_large", $"amount must be at most {MaxAmountMinor} minor units");
        }

        var currency = Currency.Parse(currencyCode);
        return currency.IsSuccess ? new Money(amountMinor, currency.Value) : currency.Error;
    }

    public override string ToString() => $"{AmountMinor} {Currency.Code} (minor units)";
}
