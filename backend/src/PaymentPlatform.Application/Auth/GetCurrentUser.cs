using Microsoft.EntityFrameworkCore;
using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.Application.Auth;

/// <summary>The signed-in user and their merchant, as the dashboard shows them.</summary>
public sealed record CurrentUser(MerchantUserId UserId, MerchantId MerchantId, string Email, string MerchantName);

/// <summary>
/// Looks up the user a validated token names. Both ids come from the token's claims; a user that no longer
/// exists, or no longer belongs to that merchant, is no one.
/// </summary>
public sealed class GetCurrentUser(PaymentsDbContext db)
{
    public Task<CurrentUser?> HandleAsync(MerchantUserId userId, MerchantId merchantId, CancellationToken cancellationToken) =>
        db.MerchantUsers.AsNoTracking()
            .Where(user => user.Id == userId && user.MerchantId == merchantId)
            .Join(
                db.Merchants,
                user => user.MerchantId,
                merchant => merchant.Id,
                (user, merchant) => new CurrentUser(user.Id, user.MerchantId, user.Email, merchant.Name))
            .SingleOrDefaultAsync(cancellationToken);
}
