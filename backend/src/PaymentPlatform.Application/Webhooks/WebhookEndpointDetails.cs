using System.Buffers.Text;
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

/// <summary>Signing secrets: <c>whsec_</c> and 32 random bytes in base64url, 49 characters.</summary>
public static class WebhookSecrets
{
    public const string Prefix = "whsec_";

    public static string New() => Prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
}

public static class WebhookEndpointErrors
{
    public static readonly DomainError NotFound =
        new("webhook_endpoint_not_found", "this merchant has no webhook endpoint; set one with PUT first");
}
