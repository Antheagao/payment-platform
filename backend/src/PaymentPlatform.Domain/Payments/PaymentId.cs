namespace PaymentPlatform.Domain.Payments;

/// <summary>A payment's identity. Version 7, so ids sort roughly by creation time.</summary>
public readonly record struct PaymentId(Guid Value)
{
    public static PaymentId New(DateTimeOffset now) => new(Guid.CreateVersion7(now));

    public override string ToString() => Value.ToString();
}
