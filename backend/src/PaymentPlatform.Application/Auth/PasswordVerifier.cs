using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using PaymentPlatform.Domain.Merchants;

namespace PaymentPlatform.Application.Auth;

/// <summary>
/// Hashes and checks passwords with ASP.NET Core Identity's hasher (PBKDF2-HMAC-SHA512, ADR 004), and spends the
/// same work on an unknown email as on a known one, so a login's timing does not reveal which accounts exist.
/// </summary>
public sealed class PasswordVerifier
{
    private readonly IPasswordHasher<MerchantUser> _hasher;

    // A hash of a random password nobody knows, made once at startup: verifying against it costs exactly what
    // verifying a real user's hash costs, and it can never succeed.
    private readonly string _unknownUserHash;

    public PasswordVerifier(IPasswordHasher<MerchantUser> hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        _hasher = hasher;
        _unknownUserHash = Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    }

    // Identity's hasher takes a user for hashers that salt per user; the default one ignores it.
    public string Hash(string password) => _hasher.HashPassword(null!, password);

    public PasswordVerificationResult Verify(MerchantUser user, string password)
    {
        ArgumentNullException.ThrowIfNull(user);
        return _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
    }

    /// <summary>Checks a stored hash of any kind of user: staff sign in with the same hasher (ADR 011).</summary>
    public PasswordVerificationResult Verify(string passwordHash, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        return _hasher.VerifyHashedPassword(null!, passwordHash, password);
    }

    /// <summary>The same work as <see cref="Verify(string, string)"/> for a login whose email matched nobody; it always fails.</summary>
    public void VerifyUnknownUser(string password) => _hasher.VerifyHashedPassword(null!, _unknownUserHash, password);
}
