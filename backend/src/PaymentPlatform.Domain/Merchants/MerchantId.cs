namespace PaymentPlatform.Domain.Merchants;

/// <summary>A merchant's identity; every merchant-owned row carries one and every query filters by it.</summary>
public readonly record struct MerchantId(Guid Value)
{
    public override string ToString() => Value.ToString();
}
