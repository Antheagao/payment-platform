using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.Application.Idempotency;

/// <summary>What a request may do with its <c>Idempotency-Key</c>.</summary>
public enum ClaimOutcome
{
    /// <summary>This request holds the key and does the work; <see cref="IdempotencyClaim.Key"/> is tracked.</summary>
    Claimed,

    /// <summary>The work is done: send back the stored response.</summary>
    Replay,

    /// <summary>Another request holds the key and is still working: <c>409</c>.</summary>
    InProgress,

    /// <summary>The key was used for a different request: <c>422</c>.</summary>
    Mismatch,
}

/// <summary>A response stored for replay: its status, its JSON body, and its <c>Location</c> header if it had one.</summary>
public sealed record StoredResponse(int Status, string Body, string? Location);

/// <summary>The result of claiming a key; hosts see the outcome and, for a replay, the stored response.</summary>
public sealed class IdempotencyClaim
{
    internal IdempotencyClaim(ClaimOutcome outcome, IdempotencyKey? key)
    {
        Outcome = outcome;
        Key = key;
    }

    public ClaimOutcome Outcome { get; }

    /// <summary>The response to send back again; set only when <see cref="Outcome"/> is <see cref="ClaimOutcome.Replay"/>.</summary>
    public StoredResponse? Response =>
        Outcome == ClaimOutcome.Replay && Key is { ResponseStatus: { } status, ResponseBody: { } body }
            ? new StoredResponse(status, body, Key.ResponseLocation)
            : null;

    /// <summary>The tracked row; a use case attaches the payment it creates to it.</summary>
    internal IdempotencyKey? Key { get; }
}

/// <summary>
/// Claims, completes, and releases idempotency keys (ADR 007). The table's unique key is the lock; a claim whose
/// request died is abandoned after <see cref="ClaimTimeout"/> and the next retry takes it over, carrying on with the
/// payment the dead request already created rather than making a second one.
/// </summary>
public sealed class IdempotencyStore(PaymentsDbContext db, TimeProvider timeProvider)
{
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    /// <summary>Three times the API's 10 s request timeout: a live request has finished or failed by then.</summary>
    public static readonly TimeSpan ClaimTimeout = TimeSpan.FromSeconds(30);

    public async Task<IdempotencyClaim> ClaimAsync(
        MerchantId merchantId, string key, byte[] fingerprint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        var now = timeProvider.GetUtcNow();
        var fresh = IdempotencyKey.Claim(merchantId, key, fingerprint, now, Retention);
        db.IdempotencyKeys.Add(fresh);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new IdempotencyClaim(ClaimOutcome.Claimed, fresh);
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            db.Entry(fresh).State = EntityState.Detached;
        }

        var existing = await db.IdempotencyKeys.SingleAsync(row => row.MerchantId == merchantId && row.Key == key, cancellationToken);
        if (existing.ExpiresAt <= now)
        {
            existing.Restart(fingerprint, now, Retention);
            return await TakeOverAsync(existing, cancellationToken);
        }

        if (!existing.Fingerprint.AsSpan().SequenceEqual(fingerprint))
        {
            return new IdempotencyClaim(ClaimOutcome.Mismatch, null);
        }

        if (existing.Status == IdempotencyStatus.Completed)
        {
            return new IdempotencyClaim(ClaimOutcome.Replay, existing);
        }

        if (existing.ClaimedAt > now - ClaimTimeout)
        {
            return new IdempotencyClaim(ClaimOutcome.InProgress, null);
        }

        existing.Reclaim(now);
        return await TakeOverAsync(existing, cancellationToken);
    }

    /// <summary>Stores the response the request sent, so every retry gets the same one.</summary>
    public async Task CompleteAsync(IdempotencyClaim claim, StoredResponse response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        var key = ClaimedKey(claim);
        key.Complete(response.Status, response.Body, response.Location);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Lets the client retry after a failure: a key with no payment is deleted, and a key whose request already
    /// created a payment is marked abandoned, so the retry takes it over and finishes that payment. Works on the
    /// database's rows rather than the tracked entity, which a failed save may have left half applied.
    /// </summary>
    public async Task ReleaseAsync(IdempotencyClaim claim)
    {
        var key = ClaimedKey(claim);
        db.ChangeTracker.Clear();
        var rows = db.IdempotencyKeys.Where(row =>
            row.MerchantId == key.MerchantId && row.Key == key.Key && row.Status == IdempotencyStatus.InProgress);
        await rows.Where(row => row.PaymentId == null).ExecuteDeleteAsync(CancellationToken.None);
        await rows.Where(row => row.PaymentId != null)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.ClaimedAt, DateTimeOffset.MinValue), CancellationToken.None);
    }

    // Of two retries taking over the same key, the row version lets exactly one save.
    private async Task<IdempotencyClaim> TakeOverAsync(IdempotencyKey key, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new IdempotencyClaim(ClaimOutcome.Claimed, key);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(key).State = EntityState.Detached;
            return new IdempotencyClaim(ClaimOutcome.InProgress, null);
        }
    }

    private static IdempotencyKey ClaimedKey(IdempotencyClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        return claim is { Outcome: ClaimOutcome.Claimed, Key: { } key }
            ? key
            : throw new InvalidOperationException("only a claimed key can be completed or released");
    }

    // 2627: primary key or unique constraint; 2601: unique index (database overlay).
    private static bool IsDuplicateKey(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2627 or 2601 };
}
