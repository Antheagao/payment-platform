namespace PaymentPlatform.Application.Acquiring;

public enum AcquirerDecision
{
    Approved,
    Declined,

    /// <summary>The acquirer did not answer in time or is down; the outcome is unknown, not negative.</summary>
    Unavailable,
}
