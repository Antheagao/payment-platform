using PaymentPlatform.Domain.Merchants;

namespace PaymentPlatform.Domain.Tests.Merchants;

public sealed class MerchantTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly MerchantId MerchantId = new(Guid.CreateVersion7(Now));

    [Fact]
    public void A_merchant_keeps_its_trimmed_name()
    {
        var merchant = Merchant.Create(MerchantId, "  Demo Merchant ", Now);

        Assert.Equal("Demo Merchant", merchant.Name);
        Assert.Equal(Now, merchant.CreatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_is_refused(string name)
    {
        Assert.ThrowsAny<ArgumentException>(() => Merchant.Create(MerchantId, name, Now));
    }

    [Fact]
    public void A_name_longer_than_the_column_is_refused()
    {
        Assert.ThrowsAny<ArgumentException>(() => Merchant.Create(MerchantId, new string('a', Merchant.NameMaxLength + 1), Now));
    }

    [Theory]
    [InlineData("demo@example.com", true)]
    [InlineData("first.last+tag@sub.example.co.uk", true)]
    [InlineData("", false)]
    [InlineData("no-at-sign", false)]
    [InlineData("@example.com", false)]
    [InlineData("demo@", false)]
    [InlineData("two@@example.com", false)]
    [InlineData("spaced out@example.com", false)]
    public void Only_plausible_emails_pass(string email, bool plausible)
    {
        Assert.Equal(plausible, MerchantUser.IsPlausibleEmail(email));
    }

    [Fact]
    public void An_email_longer_than_smtp_allows_is_not_plausible()
    {
        var local = new string('a', 64);
        var tooLong = $"{local}@{new string('b', MerchantUser.EmailMaxLength - 65)}.c";

        Assert.False(MerchantUser.IsPlausibleEmail(tooLong));
    }

    [Fact]
    public void A_user_keeps_the_trimmed_email_and_can_have_its_hash_replaced()
    {
        var user = MerchantUser.Create(MerchantUserId.New(Now), MerchantId, " demo@example.com ", "hash-v1", Now);

        user.ReplacePasswordHash("hash-v2");

        Assert.Equal("demo@example.com", user.Email);
        Assert.Equal("hash-v2", user.PasswordHash);
        Assert.Equal(MerchantId, user.MerchantId);
    }

    [Fact]
    public void A_user_needs_a_plausible_email_and_a_hash()
    {
        Assert.Throws<ArgumentException>(() => MerchantUser.Create(MerchantUserId.New(Now), MerchantId, "nope", "hash", Now));
        Assert.ThrowsAny<ArgumentException>(() => MerchantUser.Create(MerchantUserId.New(Now), MerchantId, "demo@example.com", " ", Now));
    }
}
