using System.Text.Json;

namespace PaymentPlatform.IntegrationTests.Support;

/// <summary>Reads an RFC 9457 problem response; tests assert its <c>code</c>, never its <c>detail</c> text.</summary>
public static class Problems
{
    public static async Task<JsonDocument> ReadAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    public static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        using var problem = await ReadAsync(response);
        return problem.RootElement.GetProperty("code").GetString();
    }
}
