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

        Assert.True(payment.Authorize(User, Start.AddSeconds(1)).IsSuccess);
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
        payment.Authorize(User, Start);
        payment.Capture(User, Start);

        var second = payment.Capture(User, Start.AddSeconds(1));

        Assert.Equal("payment_not_capturable", second.Error?.Code);
    }

    [Fact]
    public void Refunding_an_authorized_payment_is_rejected()
    {
        var payment = NewPayment();
        payment.Authorize(User, Start);

        Assert.Equal("payment_not_refundable", payment.Refund(User, Start).Error?.Code);
    }

    [Fact]
    public void Nothing_moves_a_failed_payment()
    {
        var payment = NewPayment();
        payment.Fail("card_declined", Settlement, Start);

        Assert.False(payment.Authorize(User, Start).IsSuccess);
        Assert.False(payment.Capture(User, Start).IsSuccess);
        Assert.False(payment.Refund(User, Start).IsSuccess);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
    }

    [Fact]
    public void Dequeuing_hands_over_the_events_once()
    {
        var payment = NewPayment();
        payment.Authorize(User, Start);

        Assert.Equal(2, payment.DequeueEvents().Count);
        Assert.Empty(payment.PendingEvents);
        Assert.Empty(payment.DequeueEvents());
    }

    private static Payment NewPayment() =>
        Payment.Create(PaymentId.New(Start), Merchant, Money.Create(1_250, "USD").Value, User, Start);
}
