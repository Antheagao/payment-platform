using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Domain.Payments;

namespace PaymentPlatform.Persistence;

public enum IdempotencyStatus
{
    /// <summary>A request holds the key and is doing the work.</summary>
    InProgress,

    /// <summary>The work is done and the response is stored for replay.</summary>
    Completed,
}

/// <summary>
/// One merchant's <c>Idempotency-Key</c> and what became of the request that first used it (ADR 007): the
/// fingerprint that a retry must match, the payment the request created, and, once done, its response.
/// </summary>
public sealed class IdempotencyKey
{
    /// <summary>The longest key the API accepts (ADR 007).</summary>
    public const int KeyMaxLength = 255;

    /// <summary>The longest stored <c>Location</c> header, such as <c>/api/v1/payments/{id}</c>.</summary>
    public const int ResponseLocationMaxLength = 200;

    private IdempotencyKey(MerchantId merchantId, string key, byte[] fingerprint, DateTimeOffset now, TimeSpan retention)
    {
        MerchantId = merchantId;
        Key = key;
        Fingerprint = fingerprint;
        Status = IdempotencyStatus.InProgress;
        CreatedAt = now;
        ClaimedAt = now;
        ExpiresAt = now + retention;
    }

    // For EF Core, which sets every property from its column.
    private IdempotencyKey()
    {
        Key = null!;
        Fingerprint = null!;
    }

    public MerchantId MerchantId { get; }

    /// <summary>As the client sent it; compared exactly (a binary collation), never case-folded.</summary>
    public string Key { get; }

    /// <summary>SHA-256 of what the request asked for; never covers a card number or CVV.</summary>
    public byte[] Fingerprint { get; private set; }

    public IdempotencyStatus Status { get; private set; }

    /// <summary>The payment the request created, set in the same transaction that first saved it.</summary>
    public PaymentId? PaymentId { get; private set; }

    public int? ResponseStatus { get; private set; }

    public string? ResponseBody { get; private set; }

    public string? ResponseLocation { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the current holder took the key; a claim older than the timeout is abandoned and may be taken over.</summary>
    public DateTimeOffset ClaimedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public static IdempotencyKey Claim(MerchantId merchantId, string key, byte[] fingerprint, DateTimeOffset now, TimeSpan retention)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(key.Length, KeyMaxLength, nameof(key));
        ArgumentNullException.ThrowIfNull(fingerprint);
        return new IdempotencyKey(merchantId, key, fingerprint, now, retention);
    }

    /// <summary>Starts over on a key whose earlier use has expired: a new request with nothing carried across.</summary>
    public void Restart(byte[] fingerprint, DateTimeOffset now, TimeSpan retention)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        Fingerprint = fingerprint;
        Status = IdempotencyStatus.InProgress;
        PaymentId = null;
        ResponseStatus = null;
        ResponseBody = null;
        ResponseLocation = null;
        CreatedAt = now;
        ClaimedAt = now;
        ExpiresAt = now + retention;
    }

    /// <summary>Takes over an abandoned claim; a payment it already created stays attached, so the work resumes.</summary>
    public void Reclaim(DateTimeOffset now)
    {
        if (Status != IdempotencyStatus.InProgress)
        {
            throw new InvalidOperationException("only a claim still in progress can be taken over");
        }

        ClaimedAt = now;
    }

    public void AttachPayment(PaymentId paymentId) => PaymentId = paymentId;

    public void Complete(int status, string body, string? location)
    {
        ArgumentException.ThrowIfNullOrEmpty(body);
        Status = IdempotencyStatus.Completed;
        ResponseStatus = status;
        ResponseBody = body;
        ResponseLocation = location;
    }
}
