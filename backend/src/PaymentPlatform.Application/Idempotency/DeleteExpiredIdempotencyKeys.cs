using Microsoft.EntityFrameworkCore;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.Application.Idempotency;

/// <summary>
/// Deletes keys whose 24 hours are up (ADR 007), in batches so one run never holds many locks; the worker runs
/// it on a schedule. A key in use when it expires is left alone until its request is done with it.
/// </summary>
public sealed class DeleteExpiredIdempotencyKeys(PaymentsDbContext db, TimeProvider timeProvider)
{
    public const int BatchSize = 1_000;

    /// <summary>Deletes until none are left or a batch comes back short, and returns how many went.</summary>
    public async Task<int> HandleAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var abandonedBefore = now - IdempotencyStore.ClaimTimeout;
        var total = 0;
        int deleted;
        do
        {
            deleted = await db.IdempotencyKeys
                .Where(key => key.ExpiresAt <= now
                    && (key.Status == IdempotencyStatus.Completed || key.ClaimedAt <= abandonedBefore))
                .OrderBy(key => key.ExpiresAt)
                .Take(BatchSize)
                .ExecuteDeleteAsync(cancellationToken);
            total += deleted;
        }
        while (deleted == BatchSize);

        return total;
    }
}
