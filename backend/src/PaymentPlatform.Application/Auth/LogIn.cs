using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PaymentPlatform.Domain;
using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.Application.Auth;

/// <summary>A login attempt as the API received it; either field may be missing, which <see cref="Validate"/> reports.</summary>
public sealed record LogInRequest(string? Email, string? Password)
{
    /// <summary>
    /// Long enough for any passphrase or password manager, short enough that hashing a huge body is not a way to
    /// spend the server's CPU.
    /// </summary>
    public const int PasswordMaxLength = 128;

    /// <summary>Field errors as codes, by snake_case field name; empty when the request can be tried.</summary>
    public IReadOnlyDictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(Email))
        {
            errors["email"] = ["email_required"];
        }
        else if (!MerchantUser.IsPlausibleEmail(Email.Trim()))
        {
            errors["email"] = ["email_invalid"];
        }

        if (string.IsNullOrEmpty(Password))
        {
            errors["password"] = ["password_required"];
        }
        else if (Password.Length > PasswordMaxLength)
        {
            errors["password"] = ["password_too_long"];
        }

        return errors;
    }
}

/// <summary>Who logged in: the token's subject and the merchant every request it carries is scoped to.</summary>
public sealed record LoggedInUser(MerchantUserId UserId, MerchantId MerchantId);

public static class AuthErrors
{
    /// <summary>One answer for an unknown email and a wrong password alike, so the API never confirms an account.</summary>
    public static readonly DomainError InvalidCredentials = new("invalid_credentials", "email or password is incorrect");
}

/// <summary>
/// Checks an email and password. The token is the API's to issue (ADR 004); this use case only decides who the
/// caller is, and upgrades a stored hash that is weaker than the hasher's current settings.
/// </summary>
public sealed partial class LogIn(PaymentsDbContext db, PasswordVerifier passwords, ILogger<LogIn> logger)
{
    /// <summary>Expects a request that passed <see cref="LogInRequest.Validate"/>.</summary>
    public async Task<Result<LoggedInUser>> HandleAsync(LogInRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Email);
        ArgumentException.ThrowIfNullOrEmpty(request.Password);

        // The column's collation is case-insensitive, so Demo@Example.com finds demo@example.com.
        var email = request.Email.Trim();
        var user = await db.MerchantUsers.SingleOrDefaultAsync(candidate => candidate.Email == email, cancellationToken);
        if (user is null)
        {
            passwords.VerifyUnknownUser(request.Password);
            LogUnknownEmail(logger);
            return AuthErrors.InvalidCredentials;
        }

        var verification = passwords.Verify(user, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            LogWrongPassword(logger, user.Id, user.MerchantId);
            return AuthErrors.InvalidCredentials;
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.ReplacePasswordHash(passwords.Hash(request.Password));
            await db.SaveChangesAsync(cancellationToken);
            LogRehashed(logger, user.Id);
        }

        LogSucceeded(logger, user.Id, user.MerchantId);
        return new LoggedInUser(user.Id, user.MerchantId);
    }

    // Failed logins are expected traffic, so Information; the email is never logged (error-handling overlay).
    [LoggerMessage(Level = LogLevel.Information, Message = "Login failed: no user has that email")]
    private static partial void LogUnknownEmail(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Login failed: wrong password for user {UserId} of merchant {MerchantId}")]
    private static partial void LogWrongPassword(ILogger logger, MerchantUserId userId, MerchantId merchantId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Password hash for user {UserId} upgraded to the hasher's current settings")]
    private static partial void LogRehashed(ILogger logger, MerchantUserId userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} of merchant {MerchantId} logged in")]
    private static partial void LogSucceeded(ILogger logger, MerchantUserId userId, MerchantId merchantId);
}
