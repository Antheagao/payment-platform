using System.Security.Cryptography;
using System.Text;

namespace PaymentPlatform.Application.Webhooks;

/// <summary>
/// The Standard Webhooks signature (standardwebhooks.com): HMAC-SHA256, keyed with the decoded secret, over
/// <c>{webhook-id}.{webhook-timestamp}.{body}</c>, sent as <c>v1,</c> and the base64 digest. Signing the id and the
/// timestamp with the body means a receiver that checks the timestamp also refuses a replayed delivery.
/// </summary>
public static class WebhookSignature
{
    public const string IdHeader = "webhook-id";
    public const string TimestampHeader = "webhook-timestamp";
    public const string SignatureHeader = "webhook-signature";

    public static string Sign(byte[] key, string id, long timestamp, string body)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(body);
        var digest = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{id}.{timestamp}.{body}"));
        return $"v1,{Convert.ToBase64String(digest)}";
    }
}
