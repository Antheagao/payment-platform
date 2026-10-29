using PaymentPlatform.Application.Cards;
using PaymentPlatform.Domain.Payments;

namespace PaymentPlatform.Application.Tests.Cards;

public sealed class CardInputTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("4242424242424242", CardBrand.Visa)]
    [InlineData("4222222222222", CardBrand.Visa)]
    [InlineData("5105105105105100", CardBrand.Mastercard)]
    [InlineData("2223003122003222", CardBrand.Mastercard)]
    [InlineData("2720999999999996", CardBrand.Mastercard)]
    [InlineData("378282246310005", CardBrand.Amex)]
    [InlineData("341111111111111", CardBrand.Amex)]
    [InlineData("6011000990139424", CardBrand.Discover)]
    [InlineData("6445644564456445", CardBrand.Discover)]
    [InlineData("6500000000000002", CardBrand.Discover)]
    public void The_brand_follows_the_leading_digits_and_length(string number, CardBrand brand)
    {
        Assert.Equal(brand, CardInput.BrandOf(number));
    }

    [Theory]
    [InlineData("2220999999999999")]
    [InlineData("2721000000000000")]
    [InlineData("5600000000000000")]
    [InlineData("37828224631000")]
    [InlineData("424242424242424")]
    [InlineData("3530111333300000")]
    public void Unknown_prefixes_and_wrong_lengths_have_no_brand(string number)
    {
        Assert.Null(CardInput.BrandOf(number));
    }

    [Theory]
    [InlineData("4242424242424242", true)]
    [InlineData("4242424242424241", false)]
    [InlineData("79927398713", true)]
    [InlineData("79927398710", false)]
    public void The_luhn_checksum_catches_a_mistyped_digit(string digits, bool valid)
    {
        Assert.Equal(valid, CardInput.PassesLuhn(digits));
    }

    [Fact]
    public void Spaces_and_dashes_between_groups_are_ignored()
    {
        var card = new CardInput("4242 4242-4242 4242", 12, 2030, "123");

        Assert.Equal("4242424242424242", card.NormalizedNumber);
        Assert.Empty(card.Validate("card", Now));
    }

    [Fact]
    public void A_card_expiring_this_month_is_still_good_and_last_month_is_not()
    {
        Assert.Empty(new CardInput("4242424242424242", 10, 2026, "123").Validate("card", Now));
        Assert.Equal(["card_expired"], new CardInput("4242424242424242", 9, 2026, "123").Validate("card", Now)["card.exp_month"]);
    }

    [Fact]
    public void The_number_and_cvv_never_print()
    {
        var card = new CardInput("4242424242424242", 12, 2030, "987");

        Assert.DoesNotContain("4242", card.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("987", card.ToString(), StringComparison.Ordinal);
    }
}
