using System.Net;
using PaymentPlatform.IntegrationTests.Support;

namespace PaymentPlatform.IntegrationTests.Errors;

public sealed class ErrorResponseTests(SqlServerFixture sqlServer) : ApiTestBase(sqlServer)
{
    [Theory]
    [InlineData("/api/v1/payments")]
    [InlineData("/api/v2/anything")]
    [InlineData("/nowhere")]
    public async Task Unknown_routes_answer_with_problem_details(string path)
    {
        using var client = Factory.CreateClient();

        using var response = await client.GetAsync(path, Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var problem = await Problems.ReadAsync(response);
        Assert.Equal("not_found", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(404, problem.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task A_wrong_method_is_a_405_problem()
    {
        using var client = Factory.CreateClient();

        using var response = await client.DeleteAsync("/health/live", Cancellation);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        using var problem = await Problems.ReadAsync(response);
        Assert.Equal("method_not_allowed", problem.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Problems_carry_the_callers_correlation_id_and_a_trace_id()
    {
        using var client = Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/nothing-here");
        request.Headers.Add("X-Correlation-Id", "merchant-retry-42");

        using var response = await client.SendAsync(request, Cancellation);

        Assert.Equal("merchant-retry-42", Assert.Single(response.Headers.GetValues("X-Correlation-Id")));
        using var problem = await Problems.ReadAsync(response);
        Assert.Equal("merchant-retry-42", problem.RootElement.GetProperty("correlation_id").GetString());
        Assert.Matches("^[0-9a-f]{32}$", problem.RootElement.GetProperty("trace_id").GetString());
        Assert.False(problem.RootElement.TryGetProperty("traceId", out _), "the camelCase framework field must be gone");
    }

    [Fact]
    public async Task Every_response_carries_the_security_headers()
    {
        using var client = Factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/nothing-here", Cancellation);

        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Contains("default-src 'none'", Assert.Single(response.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
        Assert.True(response.Headers.CacheControl?.NoStore, "payment responses must never be cached");
        Assert.False(response.Headers.Contains("Server"));
    }
}
