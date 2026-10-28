namespace PaymentPlatform.Application.Acquiring;

/// <summary>What the acquirer said. Exactly one of the three shapes, built through the factories.</summary>
public sealed record AcquirerResponse
{
    private AcquirerResponse(AcquirerDecision decision, string? reference, string? declineCode)
    {
        Decision = decision;
        Reference = reference;
        DeclineCode = declineCode;
    }

    public AcquirerDecision Decision { get; }

    /// <summary>The acquirer's reference for an approved operation; <see langword="null"/> otherwise.</summary>
    public string? Reference { get; }

    /// <summary>Why the acquirer declined, such as <c>insufficient_funds</c>; <see langword="null"/> otherwise.</summary>
    public string? DeclineCode { get; }

    public static AcquirerResponse Approved(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        return new AcquirerResponse(AcquirerDecision.Approved, reference, null);
    }

    public static AcquirerResponse Declined(string declineCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(declineCode);
        return new AcquirerResponse(AcquirerDecision.Declined, null, declineCode);
    }

    /// <summary>No answer: a timeout or an outage. Safe to retry, because the payment id is the idempotency key.</summary>
    public static AcquirerResponse Unavailable() => new(AcquirerDecision.Unavailable, null, null);
}
