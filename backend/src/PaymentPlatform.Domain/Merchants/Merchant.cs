namespace PaymentPlatform.Domain.Merchants;

/// <summary>
/// A business that takes payments. Merchants are provisioned out of band (docs/assumptions.md, assumption 1),
/// so there is no sign-up; every merchant-owned row carries the <see cref="Id"/>.
/// </summary>
public sealed class Merchant
{
    /// <summary>The longest name the API accepts and the column holds.</summary>
    public const int NameMaxLength = 200;

    private Merchant(MerchantId id, string name, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        CreatedAt = createdAt;
    }

    public MerchantId Id { get; }

    public string Name { get; }

    public DateTimeOffset CreatedAt { get; }

    /// <exception cref="ArgumentException">The name is blank or longer than <see cref="NameMaxLength"/>.</exception>
    public static Merchant Create(MerchantId id, string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmed = name.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(trimmed.Length, NameMaxLength, nameof(name));
        return new Merchant(id, trimmed, now);
    }
}
