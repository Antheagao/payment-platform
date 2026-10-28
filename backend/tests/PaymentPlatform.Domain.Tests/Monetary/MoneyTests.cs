using PaymentPlatform.Domain.Monetary;

namespace PaymentPlatform.Domain.Tests.Monetary;

public sealed class MoneyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1_250)]
    [InlineData(Money.MaxAmountMinor)]
    public void Amounts_from_one_minor_unit_to_the_cap_are_accepted(long amountMinor)
    {
        var money = Money.Create(amountMinor, "USD");

        Assert.True(money.IsSuccess, money.Error?.Code);
        Assert.Equal(amountMinor, money.Value.AmountMinor);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public void Zero_and_negative_amounts_are_rejected(long amountMinor)
    {
        Assert.Equal("amount_must_be_positive", Money.Create(amountMinor, "USD").Error?.Code);
    }

    [Theory]
    [InlineData(Money.MaxAmountMinor + 1)]
    [InlineData(long.MaxValue)]
    public void Amounts_above_the_cap_are_rejected(long amountMinor)
    {
        Assert.Equal("amount_too_large", Money.Create(amountMinor, "USD").Error?.Code);
    }

    [Theory]
    [InlineData("usd", "USD", 2)]
    [InlineData(" EUR ", "EUR", 2)]
    [InlineData("JPY", "JPY", 0)]
    public void Currency_codes_are_normalized_and_carry_their_minor_unit(string input, string code, int digits)
    {
        var currency = Currency.Parse(input);

        Assert.True(currency.IsSuccess, currency.Error?.Code);
        Assert.Equal(code, currency.Value.Code);
        Assert.Equal(digits, currency.Value.MinorUnitDigits);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("XYZ")]
    [InlineData("ÜSD")]
    public void Unknown_currencies_are_rejected(string? input)
    {
        Assert.Equal("currency_not_supported", Currency.Parse(input).Error?.Code);
    }

    [Fact]
    public void The_amount_is_checked_before_the_currency()
    {
        Assert.Equal("amount_must_be_positive", Money.Create(0, "XYZ").Error?.Code);
    }

    [Fact]
    public void Reading_the_value_of_a_failure_throws()
    {
        var failure = Money.Create(0, "USD");

        Assert.Throws<InvalidOperationException>(() => failure.Value);
    }
}
