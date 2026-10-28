namespace PaymentPlatform.Domain.Payments;

/// <summary>
/// The payment state machine: the only place that decides which status may follow which.
/// </summary>
/// <remarks>
/// Pending becomes Authorized or Failed; Authorized becomes Captured; Captured becomes Settled or Refunded;
/// Settled becomes Refunded. Failed and Refunded are terminal. Captured payments can be refunded before or
/// after settlement; partial refunds are out of scope (docs/assumptions.md, assumption 9).
/// </remarks>
public static class PaymentLifecycle
{
    public static bool CanTransition(PaymentStatus from, PaymentStatus to) => (from, to) switch
    {
        (PaymentStatus.Pending, PaymentStatus.Authorized) => true,
        (PaymentStatus.Pending, PaymentStatus.Failed) => true,
        (PaymentStatus.Authorized, PaymentStatus.Captured) => true,
        (PaymentStatus.Captured, PaymentStatus.Settled) => true,
        (PaymentStatus.Captured, PaymentStatus.Refunded) => true,
        (PaymentStatus.Settled, PaymentStatus.Refunded) => true,
        _ => false,
    };

    public static bool IsTerminal(PaymentStatus status) =>
        Enum.GetValues<PaymentStatus>().All(next => !CanTransition(status, next));

    /// <summary>The error a caller gets for moving a payment in <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static DomainError InvalidTransition(PaymentStatus from, PaymentStatus to)
    {
        var code = to switch
        {
            PaymentStatus.Authorized => "payment_not_authorizable",
            PaymentStatus.Captured => "payment_not_capturable",
            PaymentStatus.Settled => "payment_not_settleable",
            PaymentStatus.Refunded => "payment_not_refundable",
            PaymentStatus.Failed => "payment_not_pending",
            PaymentStatus.Pending => "payment_already_created",
            _ => throw new ArgumentOutOfRangeException(nameof(to), to, "unknown payment status"),
        };
        return new DomainError(code, $"a {Describe(from)} payment cannot move to {Describe(to)}");
    }

    private static string Describe(PaymentStatus status) => status.ToString().ToLowerInvariant();
}
