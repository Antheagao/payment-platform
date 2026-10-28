using PaymentPlatform.Domain.Payments;

namespace PaymentPlatform.Domain.Tests.Payments;

public sealed class PaymentLifecycleTests
{
    // The whole state machine as data: anything not listed here must be rejected.
    private static readonly HashSet<(PaymentStatus From, PaymentStatus To)> Allowed =
    [
        (PaymentStatus.Pending, PaymentStatus.Authorized),
        (PaymentStatus.Pending, PaymentStatus.Failed),
        (PaymentStatus.Authorized, PaymentStatus.Captured),
        (PaymentStatus.Captured, PaymentStatus.Settled),
        (PaymentStatus.Captured, PaymentStatus.Refunded),
        (PaymentStatus.Settled, PaymentStatus.Refunded),
    ];

    public static TheoryData<PaymentStatus, PaymentStatus> EveryPair()
    {
        var data = new TheoryData<PaymentStatus, PaymentStatus>();
        foreach (var from in Enum.GetValues<PaymentStatus>())
        {
            foreach (var to in Enum.GetValues<PaymentStatus>())
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void Only_the_documented_transitions_are_allowed(PaymentStatus from, PaymentStatus to)
    {
        Assert.Equal(Allowed.Contains((from, to)), PaymentLifecycle.CanTransition(from, to));
    }

    [Theory]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Refunded)]
    public void Failed_and_refunded_are_terminal(PaymentStatus status)
    {
        Assert.True(PaymentLifecycle.IsTerminal(status));
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Authorized)]
    [InlineData(PaymentStatus.Captured)]
    [InlineData(PaymentStatus.Settled)]
    public void Every_other_status_has_a_way_forward(PaymentStatus status)
    {
        Assert.False(PaymentLifecycle.IsTerminal(status));
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void Every_rejected_transition_has_a_stable_code_and_a_readable_message(PaymentStatus from, PaymentStatus to)
    {
        var error = PaymentLifecycle.InvalidTransition(from, to);

        Assert.Matches("^payment_[a-z_]+$", error.Code);
        Assert.Equal($"a {from.ToString().ToLowerInvariant()} payment cannot move to {to.ToString().ToLowerInvariant()}", error.Message);
    }
}
