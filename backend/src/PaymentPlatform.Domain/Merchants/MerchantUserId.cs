namespace PaymentPlatform.Domain.Merchants;

/// <summary>A dashboard user's identity; the <c>sub</c> claim of their token and the actor on their changes.</summary>
public readonly record struct MerchantUserId(Guid Value)
{
    public static MerchantUserId New(DateTimeOffset now) => new(Guid.CreateVersion7(now));

    public override string ToString() => Value.ToString();
}
