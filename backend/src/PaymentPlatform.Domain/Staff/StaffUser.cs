using PaymentPlatform.Domain.Merchants;

namespace PaymentPlatform.Domain.Staff;

/// <summary>
/// A platform operator (ADR 011): someone who looks after the platform itself, across every merchant, and so
/// belongs to none. Kept apart from <see cref="MerchantUser"/>, so no check on one kind can let the other through.
/// </summary>
/// <remarks>As for a merchant user, the application's hasher makes and checks the hash; the domain only keeps it.</remarks>
public sealed class StaffUser
{
    private StaffUser(StaffUserId id, string email, string passwordHash, DateTimeOffset createdAt)
    {
        Id = id;
        Email = email;
        PasswordHash = passwordHash;
        CreatedAt = createdAt;
    }

    public StaffUserId Id { get; }

    /// <summary>As given, trimmed; unique without regard to case, which the database collation enforces.</summary>
    public string Email { get; }

    public string PasswordHash { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    /// <exception cref="ArgumentException">The email or hash is blank, or the email is not a plausible address.</exception>
    public static StaffUser Create(StaffUserId id, string email, string passwordHash, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        var trimmed = email.Trim();
        if (!MerchantUser.IsPlausibleEmail(trimmed))
        {
            throw new ArgumentException("not a plausible email address", nameof(email));
        }

        return new StaffUser(id, trimmed, passwordHash, now);
    }

    public void ReplacePasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }
}
