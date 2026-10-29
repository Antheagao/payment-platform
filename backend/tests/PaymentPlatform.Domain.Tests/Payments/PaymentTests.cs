using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Domain.Monetary;
using PaymentPlatform.Domain.Payments;

namespace PaymentPlatform.Domain.Tests.Payments;

public sealed class PaymentTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly MerchantId Merchant = new(Guid.Parse("8f0d5d7e-6f2a-4b0e-9a51-3c1f2a7d9e10"));
    private static readonly Actor User = Actor.MerchantUser(Guid.Parse("1b9a4c2e-0d3f-4e5a-8b6c-7d8e9f0a1b2c"));
    private static readonly Actor Settlement = Actor.System("settlement");
    private static readonly PaymentCard Card = new("tok_test_visa", CardBrand.Visa, "4242", 12, 2030);

    private const string AuthorizationReference = "sim_auth_0001";

    [Fact]
    public void A_new_payment_is_pending_and_records_its_creation()
    {
        var payment = NewPayment();

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(Start, payment.UpdatedAt);
        var created = Assert.Single(payment.PendingEvents);
        Assert.Null(created.From);
        Assert.Equal(PaymentStatus.Pending, created.To);
        Assert.Equal(User, created.Actor);
        Assert.Equal(Start, created.OccurredAt);
    }

    [Fact]
    public void The_happy_path_records_every_step_with_its_actor_and_time()
    {
        var payment = NewPayment();

        Assert.True(payment.Authorize(AuthorizationReference, User, Start.AddSeconds(1)).IsSuccess);
        Assert.True(payment.Capture(User, Start.AddMinutes(5)).IsSuccess);
        Assert.True(payment.Settle(Settlement, Start.AddDays(1)).IsSuccess);
        Assert.True(payment.Refund(User, Start.AddDays(2)).IsSuccess);

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(Start.AddDays(2), payment.UpdatedAt);
        Assert.Equal(
            [
                (null, PaymentStatus.Pending, User),
                (PaymentStatus.Pending, PaymentStatus.Authorized, User),
                (PaymentStatus.Authorized, PaymentStatus.Captured, User),
                (PaymentStatus.Captured, PaymentStatus.Settled, Settlement),
                (PaymentStatus.Settled, PaymentStatus.Refunded, User),
            ],
            payment.PendingEvents.Select(change => (change.From, change.To, change.Actor)));
    }

    [Fact]
    public void A_decline_fails_the_payment_and_keeps_the_reason()
    {
        var payment = NewPayment();

        var result = payment.Fail("card_declined", Settlement, Start.AddSeconds(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal("card_declined", payment.PendingEvents[^1].Reason);
    }

    [Fact]
    public void Capturing_a_pending_payment_is_rejected_and_changes_nothing()
    {
        var payment = NewPayment();

        var result = payment.Capture(User, Start.AddMinutes(1));

        Assert.False(result.IsSuccess);
        Assert.Equal("payment_not_capturable", result.Error.Code);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(Start, payment.UpdatedAt);
        Assert.Single(payment.PendingEvents);
    }

    [Fact]
    public void Capturing_twice_is_rejected()
    {
        var payment = NewPayment();
        payment.Authorize(AuthorizationReference, User, Start);
        payment.Capture(User, Start);

        var second = payment.Capture(User, Start.AddSeconds(1));

        Assert.Equal("payment_not_capturable", second.Error?.Code);
    }

    [Fact]
    public void Refunding_an_authorized_payment_is_rejected()
    {
        var payment = NewPayment();
        payment.Authorize(AuthorizationReference, User, Start);

        Assert.Equal("payment_not_refundable", payment.Refund(User, Start).Error?.Code);
    }

    [Fact]
    public void Nothing_moves_a_failed_payment()
    {
        var payment = NewPayment();
        payment.Fail("card_declined", Settlement, Start);

        Assert.False(payment.Authorize(AuthorizationReference, User, Start).IsSuccess);
        Assert.False(payment.Capture(User, Start).IsSuccess);
        Assert.False(payment.Refund(User, Start).IsSuccess);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
    }

    [Fact]
    public void Dequeuing_hands_over_the_events_once()
    {
        var payment = NewPayment();
        payment.Authorize(AuthorizationReference, User, Start);

        Assert.Equal(2, payment.DequeueEvents().Count);
        Assert.Empty(payment.PendingEvents);
        Assert.Empty(payment.DequeueEvents());
    }

    [Fact]
    public void Authorizing_keeps_the_acquirer_reference()
    {
        var payment = NewPayment();

        payment.Authorize(AuthorizationReference, User, Start);

        Assert.Equal(AuthorizationReference, payment.AuthorizationReference);
        Assert.Null(payment.FailureReason);
    }

    [Fact]
    public void A_decline_keeps_its_reason_on_the_payment()
    {
        var payment = NewPayment();

        payment.Fail("insufficient_funds", Settlement, Start);

        Assert.Equal("insufficient_funds", payment.FailureReason);
        Assert.Null(payment.AuthorizationReference);
    }

    [Fact]
    public void A_rejected_move_leaves_the_references_alone()
    {
        var payment = NewPayment();
        payment.Fail("card_declined", Settlement, Start);

        Assert.False(payment.Authorize("sim_auth_late", User, Start).IsSuccess);
        Assert.Null(payment.AuthorizationReference);

        var authorized = NewPayment();
        authorized.Authorize(AuthorizationReference, User, Start);
        Assert.False(authorized.Fail("too_late", Settlement, Start).IsSuccess);
        Assert.Null(authorized.FailureReason);
    }

    [Fact]
    public void A_payment_keeps_its_card_and_merchant_reference()
    {
        var payment = Payment.Create(
            PaymentId.New(Start), Merchant, Money.Create(500, "EUR").Value, Card, "order-1001", User, Start);

        Assert.Same(Card, payment.Card);
        Assert.Equal("order-1001", payment.MerchantReference);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void A_blank_merchant_reference_is_refused(string reference)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            Payment.Create(PaymentId.New(Start), Merchant, Money.Create(500, "EUR").Value, Card, reference, User, Start));
    }

    [Fact]
    public void A_merchant_reference_longer_than_the_column_is_refused()
    {
        var reference = new string('r', Payment.MerchantReferenceMaxLength + 1);

        Assert.ThrowsAny<ArgumentException>(() =>
            Payment.Create(PaymentId.New(Start), Merchant, Money.Create(500, "EUR").Value, Card, reference, User, Start));
    }

    [Theory]
    [InlineData("", "4242", 12, 2030)]
    [InlineData("tok_1", "424", 12, 2030)]
    [InlineData("tok_1", "42a2", 12, 2030)]
    [InlineData("tok_1", "4242", 0, 2030)]
    [InlineData("tok_1", "4242", 13, 2030)]
    [InlineData("tok_1", "4242", 12, 1999)]
    public void A_card_needs_a_token_four_digits_and_a_real_expiry(string token, string last4, int month, int year)
    {
        Assert.ThrowsAny<ArgumentException>(() => new PaymentCard(token, CardBrand.Visa, last4, month, year));
    }

    private static Payment NewPayment() =>
        Payment.Create(PaymentId.New(Start), Merchant, Money.Create(1_250, "USD").Value, Card, null, User, Start);
}
