namespace PaymentPlatform.Application.Acquiring;

/// <summary>
/// Card tokens that make the <see cref="SimulatedAcquirer"/> take a specific branch, so every outcome is
/// reproducible in tests and demos. Any other token is approved.
/// </summary>
/// <remarks>
/// <see cref="Cards.SimulatedCardVault"/> issues these for its scenario test cards (<c>TestCards</c>).
/// </remarks>
public static class SimulatedCardTokens
{
    public const string CardDeclined = "tok_sim_card_declined";

    public const string InsufficientFunds = "tok_sim_insufficient_funds";

    /// <summary>The acquirer does not answer; the payment stays <c>Pending</c> for reconciliation.</summary>
    public const string AcquirerUnavailable = "tok_sim_acquirer_unavailable";

    /// <summary>
    /// The acquirer does not answer for the first <see cref="SimulatedAcquirer.LateAnswerAfter"/> of the payment's
    /// life, then approves: a payment that reconciliation finishes.
    /// </summary>
    public const string AcquirerLate = "tok_sim_acquirer_late";
}
