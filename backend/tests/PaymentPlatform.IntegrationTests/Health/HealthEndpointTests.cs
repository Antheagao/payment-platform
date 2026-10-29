using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using PaymentPlatform.IntegrationTests.Support;

namespace PaymentPlatform.IntegrationTests.Health;

public sealed class HealthEndpointTests(SqlServerFixture sqlServer) : ApiTestBase(sqlServer)
{
    // The readiness deadline (three seconds) plus room for a loaded CI runner, and less than the dashboard's five.
    private static readonly TimeSpan ReadinessBudget = TimeSpan.FromSeconds(4);

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

        await AssertReadinessFailsFastAsync(factory);
    }

    [Fact]
    public async Task Readiness_answers_by_its_deadline_when_the_database_accepts_and_never_replies()
    {
        // The kernel completes the handshake for queued connections, so the client connects and then waits
        // on a server that never speaks: the case where it ignores cancellation until its own timeout.
        var silentServer = new TcpListener(IPAddress.Loopback, 0);
        silentServer.Start();
        try
        {
            var port = ((IPEndPoint)silentServer.LocalEndpoint).Port;
            await using var factory = new ApiFactory(
                $"Server=127.0.0.1,{port};Database=payments;User Id=sa;Password=unused;TrustServerCertificate=True");

            await AssertReadinessFailsFastAsync(factory);
        }
        finally
        {
            silentServer.Stop();
        }
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

    // The first probe after an outage and every one after it: the dashboard waits five seconds, so an answer
    // inside the three-second deadline is what lets it say "not ready" instead of "unreachable".
    private static async Task AssertReadinessFailsFastAsync(ApiFactory factory)
    {
        using var client = factory.CreateClient();
        for (var probe = 1; probe <= 3; probe++)
        {
            var stopwatch = Stopwatch.StartNew();
            using var response = await client.GetAsync("/health/ready", Cancellation);
            stopwatch.Stop();

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.True(stopwatch.Elapsed < ReadinessBudget, $"probe {probe} took {stopwatch.Elapsed}");
            var text = await response.Content.ReadAsStringAsync(Cancellation);
            using var body = JsonDocument.Parse(text);
            Assert.Equal("unhealthy", body.RootElement.GetProperty("checks").GetProperty("database").GetString());
            Assert.DoesNotContain("127.0.0.1", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Password", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
    }
}
