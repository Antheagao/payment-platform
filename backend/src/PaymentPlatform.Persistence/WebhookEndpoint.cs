using PaymentPlatform.Domain.Merchants;

namespace PaymentPlatform.Persistence;

/// <summary>
/// Where a merchant receives events, and the secret their deliveries are signed with (docs/assumptions.md,
/// assumption 11: one endpoint per merchant). A disabled endpoint gets no new deliveries.
/// </summary>
/// <remarks>
/// The secret is stored as issued, because signing needs it; Azure SQL encrypts it at rest with the rest of the
/// database, and moving secrets to Key Vault with only their names here is a production step (production.md).
/// </remarks>
public sealed class WebhookEndpoint
{
    public const int UrlMaxLength = 2048;

    public const int SecretMaxLength = 64;

    private WebhookEndpoint(Guid id, MerchantId merchantId, string url, string secret, DateTimeOffset now)
    {
        Id = id;
        MerchantId = merchantId;
        Url = url;
        Secret = secret;
        Enabled = true;
        CreatedAt = now;
        UpdatedAt = now;
    }

    // For EF Core, which sets every property from its column.
    private WebhookEndpoint()
    {
        Url = null!;
        Secret = null!;
    }

    public Guid Id { get; }

    public MerchantId MerchantId { get; }

    public string Url { get; private set; }

    /// <summary>The HMAC-SHA256 key, shown to the merchant once, when it is issued.</summary>
    public string Secret { get; private set; }

    public bool Enabled { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static WebhookEndpoint Create(Guid id, MerchantId merchantId, string url, string secret, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(url.Length, UrlMaxLength, nameof(url));
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(secret.Length, SecretMaxLength, nameof(secret));
        return new WebhookEndpoint(id, merchantId, url, secret, now);
    }

    /// <summary>Points the endpoint at <paramref name="url"/> and turns it on or off; the secret stays, so receivers keep verifying.</summary>
    public void Update(string url, bool enabled, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(url.Length, UrlMaxLength, nameof(url));
        Url = url;
        Enabled = enabled;
        UpdatedAt = now;
    }

    /// <summary>Replaces the secret; deliveries from now on, retries included, are signed with the new one.</summary>
    public void RotateSecret(string secret, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(secret.Length, SecretMaxLength, nameof(secret));
        Secret = secret;
        UpdatedAt = now;
    }

    /// <summary>Stops new deliveries, as a <c>410 Gone</c> from the receiver or the merchant's own request does (ADR 003).</summary>
    public void Disable(DateTimeOffset now)
    {
        Enabled = false;
        UpdatedAt = now;
    }
}
