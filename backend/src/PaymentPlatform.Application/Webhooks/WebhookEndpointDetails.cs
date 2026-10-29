using System.Security.Cryptography;
using PaymentPlatform.Domain;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.Application.Webhooks;

/// <summary>
/// A merchant's webhook endpoint as the API shows it. <see cref="Secret"/> is set only on the answer that issues one
/// (the first set and each rotation): it is shown once, and never read back afterwards.
/// </summary>
public sealed record WebhookEndpointDetails(string Url, bool Enabled, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? Secret)
{
    internal static WebhookEndpointDetails From(WebhookEndpoint endpoint, bool withSecret) =>
        new(endpoint.Url, endpoint.Enabled, endpoint.CreatedAt, endpoint.UpdatedAt, withSecret ? endpoint.Secret : null);
}

/// <summary>
/// Signing secrets in the Standard Webhooks form, so a receiver can verify with any of its libraries: <c>whsec_</c>
/// and 32 random bytes in standard base64 (50 characters). The key is the decoded bytes, not the text.
/// </summary>
public static class WebhookSecrets
{
    public const string Prefix = "whsec_";

    public static string New() => Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>The HMAC key a secret stands for; null for text that is not a secret in this form.</summary>
    public static byte[]? KeyOf(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        if (!secret.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var key = new byte[secret.Length];
        return Convert.TryFromBase64String(secret[Prefix.Length..], key, out var written) && written >= 16 ? key[..written] : null;
    }
}

public static class WebhookEndpointErrors
{
    public static readonly DomainError NotFound =
        new("webhook_endpoint_not_found", "this merchant has no webhook endpoint; set one with PUT first");
}
