namespace PaymentPlatform.Domain.Merchants;

/// <summary>
/// A person who signs in to the dashboard. A user belongs to exactly one merchant (docs/assumptions.md,
/// assumption 2), and everything they see is scoped to it.
/// </summary>
/// <remarks>
/// The password hash is produced and checked by the application's password hasher; the domain only keeps it,
/// so no hashing scheme leaks in here.
/// </remarks>
public sealed class MerchantUser
{
    /// <summary>The longest email address SMTP allows (RFC 5321), which is also the column's length.</summary>
    public const int EmailMaxLength = 254;

    private MerchantUser(MerchantUserId id, MerchantId merchantId, string email, string passwordHash, DateTimeOffset createdAt)
    {
        Id = id;
        MerchantId = merchantId;
        Email = email;
        PasswordHash = passwordHash;
        CreatedAt = createdAt;
    }

    public MerchantUserId Id { get; }

    public MerchantId MerchantId { get; }

    /// <summary>As given, trimmed; unique without regard to case, which the database collation enforces.</summary>
    public string Email { get; }

    public string PasswordHash { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    /// <exception cref="ArgumentException">The email or hash is blank, or the email is not a plausible address.</exception>
    public static MerchantUser Create(
        MerchantUserId id, MerchantId merchantId, string email, string passwordHash, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        var trimmed = email.Trim();
        if (!IsPlausibleEmail(trimmed))
        {
            throw new ArgumentException("not a plausible email address", nameof(email));
        }

        return new MerchantUser(id, merchantId, trimmed, passwordHash, now);
    }

    /// <summary>
    /// One <c>@</c> with something on each side and no whitespace, within <see cref="EmailMaxLength"/>. Anything
    /// stricter belongs to sending a confirmation email, which provisioning out of band does not need.
    /// </summary>
    public static bool IsPlausibleEmail(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        var at = email.IndexOf('@', StringComparison.Ordinal);
        return email.Length <= EmailMaxLength
            && at > 0
            && at == email.LastIndexOf('@')
            && at < email.Length - 1
            && !email.Any(char.IsWhiteSpace);
    }

    /// <summary>Replaces the hash after a login found the stored one weaker than the hasher's current settings.</summary>
    public void ReplacePasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }
}
