namespace PaymentPlatform.Domain.Staff;

/// <summary>A platform operator's identity (ADR 011); the <c>sub</c> claim of their token.</summary>
public readonly record struct StaffUserId(Guid Value)
{
    public static StaffUserId New(DateTimeOffset now) => new(Guid.CreateVersion7(now));

    public override string ToString() => Value.ToString();
}
