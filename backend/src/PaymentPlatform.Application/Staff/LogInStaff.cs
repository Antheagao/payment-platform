using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PaymentPlatform.Application.Auth;
using PaymentPlatform.Domain;
using PaymentPlatform.Domain.Staff;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.Application.Staff;

/// <summary>Who signed in as staff: the token's subject, with no merchant to scope it to (ADR 011).</summary>
public sealed record LoggedInStaff(StaffUserId StaffId);

/// <summary>
/// Checks a staff user's email and password, as <see cref="LogIn"/> does a merchant user's: one answer for an
/// unknown email and a wrong password, the same hashing work for both, and a weaker stored hash upgraded.
/// </summary>
public sealed partial class LogInStaff(PaymentsDbContext db, PasswordVerifier passwords, ILogger<LogInStaff> logger)
{
    /// <summary>Expects a request that passed <see cref="LogInRequest.Validate"/>.</summary>
    public async Task<Result<LoggedInStaff>> HandleAsync(LogInRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Email);
        ArgumentException.ThrowIfNullOrEmpty(request.Password);

        var email = request.Email.Trim();
        var staff = await db.StaffUsers.SingleOrDefaultAsync(candidate => candidate.Email == email, cancellationToken);
        if (staff is null)
        {
            passwords.VerifyUnknownUser(request.Password);
            LogUnknownEmail(logger);
            return AuthErrors.InvalidCredentials;
        }

        var verification = passwords.Verify(staff.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            LogWrongPassword(logger, staff.Id);
            return AuthErrors.InvalidCredentials;
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            staff.ReplacePasswordHash(passwords.Hash(request.Password));
            await db.SaveChangesAsync(cancellationToken);
        }

        LogSucceeded(logger, staff.Id);
        return new LoggedInStaff(staff.Id);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Staff login failed: no staff user has that email")]
    private static partial void LogUnknownEmail(ILogger logger);

    // A wrong staff password is worth more attention than a merchant's, but it is still expected traffic.
    [LoggerMessage(Level = LogLevel.Information, Message = "Staff login failed: wrong password for staff user {StaffId}")]
    private static partial void LogWrongPassword(ILogger logger, StaffUserId staffId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Staff user {StaffId} logged in")]
    private static partial void LogSucceeded(ILogger logger, StaffUserId staffId);
}
