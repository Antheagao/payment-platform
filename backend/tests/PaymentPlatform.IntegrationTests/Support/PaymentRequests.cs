using System.Net.Http.Json;
using System.Text;

namespace PaymentPlatform.IntegrationTests.Support;

/// <summary><c>POST /api/v1/payments</c> with an <c>Idempotency-Key</c>: a fresh one unless the test names it.</summary>
public static class PaymentRequests
{
    public const string Path = "/api/v1/payments";

    public static Task<HttpResponseMessage> CreateAsync(HttpClient client, object body, string? idempotencyKey = null) =>
        SendAsync(client, JsonContent.Create(body), idempotencyKey);

    public static Task<HttpResponseMessage> CreateRawAsync(HttpClient client, string json, string? idempotencyKey = null) =>
        SendAsync(client, new StringContent(json, Encoding.UTF8, "application/json"), idempotencyKey);

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpContent content, string? idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = content };
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
