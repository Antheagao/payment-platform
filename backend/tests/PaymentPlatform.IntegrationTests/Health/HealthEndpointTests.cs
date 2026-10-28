using System.Diagnostics;
using System.Net;
using System.Text.Json;
using PaymentPlatform.IntegrationTests.Support;

namespace PaymentPlatform.IntegrationTests.Health;

public sealed class HealthEndpointTests(SqlServerFixture sqlServer) : ApiTestBase(sqlServer)
{
    [Fact]
    public async Task Liveness_answers_with_only_the_self_check()
    {
        using var client = Factory.CreateClient();

        using var response = await client.GetAsync("/health/live", Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal("healthy", body.RootElement.GetProperty("status").GetString());
        var checks = body.RootElement.GetProperty("checks").EnumerateObject().Select(check => check.Name);
        Assert.Equal(["self"], checks);
    }

    [Fact]
    public async Task Readiness_is_healthy_against_a_migrated_database()
    {
        using var client = Factory.CreateClient();

        using var response = await client.GetAsync("/health/ready", Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal("healthy", body.RootElement.GetProperty("checks").GetProperty("database").GetString());
    }

    [Fact]
    public async Task Readiness_fails_fast_and_says_nothing_about_why_when_the_database_is_gone()
    {
        await using var factory = new ApiFactory(ApiFactory.UnreachableDatabase);
        using var client = factory.CreateClient();
        var stopwatch = Stopwatch.StartNew();

        using var response = await client.GetAsync("/health/ready", Cancellation);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"readiness took {stopwatch.Elapsed}");
        var text = await response.Content.ReadAsStringAsync(Cancellation);
        using var body = JsonDocument.Parse(text);
        Assert.Equal("unhealthy", body.RootElement.GetProperty("checks").GetProperty("database").GetString());
        Assert.DoesNotContain("127.0.0.1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Liveness_stays_green_when_the_database_is_gone()
    {
        await using var factory = new ApiFactory(ApiFactory.UnreachableDatabase);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live", Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Probes_answer_head_requests(string path)
    {
        using var client = Factory.CreateClient();

        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path), Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
    }
}
