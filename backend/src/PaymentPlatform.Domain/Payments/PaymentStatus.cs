namespace PaymentPlatform.Domain.Payments;

/// <summary>
/// Where a payment is in its lifecycle. Persisted and serialized by name, so renaming a member is a
/// migration and an API change; the allowed moves between members live in <see cref="PaymentLifecycle"/>.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Recorded, and the acquirer has not answered yet.</summary>
    Pending,

    /// <summary>The acquirer approved and reserved the funds.</summary>
    Authorized,

    /// <summary>The merchant claimed the authorized funds.</summary>
    Captured,

    /// <summary>The acquirer paid the captured funds out to the merchant.</summary>
    Settled,

    /// <summary>The acquirer declined; terminal.</summary>
    Failed,

    /// <summary>The funds went back to the cardholder; terminal.</summary>
    Refunded,
}
