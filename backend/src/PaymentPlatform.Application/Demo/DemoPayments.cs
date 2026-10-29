using System.Globalization;
using PaymentPlatform.Application.Acquiring;
using PaymentPlatform.Application.Cards;
using PaymentPlatform.Domain;
using PaymentPlatform.Domain.Monetary;
using PaymentPlatform.Domain.Payments;

namespace PaymentPlatform.Application.Demo;

/// <summary>
/// The demo merchant's sample payments: every status, each reached through the domain so it carries a real audit
/// trail, spread over the last 90 days. Ids, amounts, references, and cards are fixed, so a demo can name a
/// payment and seeding again adds nothing.
/// </summary>
public static class DemoPayments
{
    /// <summary>The correlation id on every seeded audit row, so seeded history is never mistaken for a request's.</summary>
    public const string CorrelationId = "demo-seed";

    /// <summary>What happens to a seeded payment after it is created.</summary>
    public enum Path
    {
        LeftPending,
        Authorized,
        Captured,
        Settled,
        RefundedAfterCapture,
        RefundedAfterSettlement,
        Declined,
        InsufficientFunds,
    }

    /// <param name="Number">The seed's position, which fixes the payment's id.</param>
    /// <param name="DaysAgo">When the payment was created, relative to the first seeding.</param>
    public sealed record Spec(int Number, long AmountMinor, string Currency, string Reference, string Card, Path Path, double DaysAgo);

    /// <summary>
    /// Thirty payments: ten settled, five captured, four authorized, two refunded from each of captured and
    /// settled, two declined, two for insufficient funds, and three left pending by a silent acquirer.
    /// </summary>
    public static readonly IReadOnlyList<Spec> All =
    [
        new(1, 2_599, "USD", "INV-1001", "4242424242424242", Path.Settled, 88.2),
        new(2, 18_000, "USD", "INV-1002", "5555555555554444", Path.Settled, 84.6),
        new(3, 4_250, "EUR", "INV-1003", "4242424242424242", Path.Settled, 80.1),
        new(4, 129_900, "USD", "INV-1004", "378282246310005", Path.RefundedAfterSettlement, 77.5),
        new(5, 7_500, "GBP", "INV-1005", "6011111111111117", Path.Settled, 73.9),
        new(6, 3_200, "USD", "INV-1006", "4000000000000002", Path.Declined, 70.3),
        new(7, 54_000, "JPY", "INV-1007", "5555555555554444", Path.Settled, 66.8),
        new(8, 999, "USD", "INV-1008", "4242424242424242", Path.Settled, 62.4),
        new(9, 45_000, "CAD", "INV-1009", "378282246310005", Path.RefundedAfterSettlement, 58.7),
        new(10, 12_345, "USD", "INV-1010", "4000000000009995", Path.InsufficientFunds, 55.2),
        new(11, 6_000, "AUD", "INV-1011", "4242424242424242", Path.Settled, 51.6),
        new(12, 22_050, "EUR", "INV-1012", "5555555555554444", Path.Settled, 47.9),
        new(13, 1_500, "USD", "INV-1013", "6011111111111117", Path.Settled, 44.3),
        new(14, 89_999, "USD", "INV-1014", "4242424242424242", Path.Settled, 40.8),
        new(15, 3_499, "GBP", "INV-1015", "4000000000000002", Path.Declined, 37.1),
        new(16, 15_000, "USD", "INV-1016", "378282246310005", Path.Captured, 33.5),
        new(17, 8_800, "EUR", "INV-1017", "5555555555554444", Path.RefundedAfterCapture, 29.9),
        new(18, 2_000, "USD", "INV-1018", "4242424242424242", Path.Captured, 26.4),
        new(19, 41_500, "USD", "INV-1019", "4000000000009995", Path.InsufficientFunds, 22.7),
        new(20, 5_600, "CAD", "INV-1020", "6011111111111117", Path.Captured, 19.2),
        new(21, 67_300, "USD", "INV-1021", "4242424242424242", Path.RefundedAfterCapture, 15.6),
        new(22, 9_900, "AUD", "INV-1022", "5555555555554444", Path.Captured, 12.1),
        new(23, 3_000, "USD", "INV-1023", "4000000000000119", Path.LeftPending, 9.4),
        new(24, 27_500, "USD", "INV-1024", "378282246310005", Path.Captured, 6.8),
        new(25, 14_200, "EUR", "INV-1025", "4242424242424242", Path.Authorized, 4.3),
        new(26, 1_250, "USD", "INV-1026", "5555555555554444", Path.Authorized, 2.9),
        new(27, 5_000, "USD", "INV-1027", "4000000000000119", Path.LeftPending, 1.6),
        new(28, 76_000, "JPY", "INV-1028", "6011111111111117", Path.Authorized, 0.8),
        new(29, 31_900, "USD", "INV-1029", "4242424242424242", Path.Authorized, 0.3),
        new(30, 4_400, "GBP", "INV-1030", "4000000000000119", Path.LeftPending, 0.1),
    ];

    /// <summary>A seeded payment's id: fixed, so docs/demo.md can name one.</summary>
    public static PaymentId IdOf(int number) =>
        new(Guid.Parse(string.Create(CultureInfo.InvariantCulture, $"019a2c00-0000-7000-8000-{1000 + number:D12}")));

    /// <summary>Builds one seeded payment, walked through the domain from <c>Pending</c> to where its spec ends.</summary>
    public static Payment Build(Spec spec, ICardVault vault, Actor user, DateTimeOffset seededAt)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(vault);
        var createdAt = seededAt - TimeSpan.FromDays(spec.DaysAgo);
        var card = vault.Tokenize(new CardInput(spec.Card, 12, seededAt.Year + 2, spec.Card.StartsWith('3') ? "1234" : "123")).Value;
        var id = IdOf(spec.Number);
        var payment = Payment.Create(id, DemoSeed.MerchantId, Money.Create(spec.AmountMinor, spec.Currency).Value, card, spec.Reference, user, createdAt);

        // The simulated acquirer's own reference format, so a seeded payment can be captured and refunded for real.
        var authorization = SimulatedAcquirer.AuthorizationPrefix + id.Value.ToString("N");
        var answered = createdAt.AddSeconds(2);
        var settlement = Actor.System("settlement");
        Expect(spec.Path switch
        {
            Path.LeftPending => Result.Success(),
            Path.Declined => payment.Fail("card_declined", user, answered),
            Path.InsufficientFunds => payment.Fail("insufficient_funds", user, answered),
            _ => payment.Authorize(authorization, user, answered),
        });
        if (spec.Path is Path.Captured or Path.Settled or Path.RefundedAfterCapture or Path.RefundedAfterSettlement)
        {
            Expect(payment.Capture(user, createdAt.AddHours(1)));
        }

        if (spec.Path is Path.Settled or Path.RefundedAfterSettlement)
        {
            Expect(payment.Settle(settlement, createdAt.AddDays(1)));
        }

        if (spec.Path is Path.RefundedAfterCapture or Path.RefundedAfterSettlement)
        {
            Expect(payment.Refund(user, createdAt.AddDays(2)));
        }

        return payment;
    }

    // A spec that walks an illegal path is a bug in the list above; fail the seed rather than write it.
    private static void Expect(Result result)
    {
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"a demo payment's path is not a legal lifecycle: {result.Error.Code}");
        }
    }
}
