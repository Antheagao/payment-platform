using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentPlatform.Api.Auth;
using PaymentPlatform.IntegrationTests.Support;

namespace PaymentPlatform.IntegrationTests.Auth;

/// <summary>
/// The per-address login limit, per person (ADR 010): the dashboard names the person's address under its key, and
/// nothing else may name one.
/// </summary>
public sealed class ClientAddressTests(SqlServerFixture sqlServer) : ApiTestBase(sqlServer)
{
    private const string LoginPath = "/api/v1/auth/login";
    private const string DashboardKey = "local-test-dashboard-key-0123456789abcdef";

    [Fact]
    public async Task With_the_dashboards_key_each_named_address_has_its_own_limit()
    {
        await using var factory = Limited(builder => builder.UseSetting("Dashboard:Key", DashboardKey));
        using var client = factory.CreateClient();

        using var first = await LogInAsync(client, "203.0.113.7", DashboardKey);
        using var second = await LogInAsync(client, "203.0.113.7", DashboardKey);
        using var third = await LogInAsync(client, "203.0.113.7", DashboardKey);
        using var someoneElse = await LogInAsync(client, "198.51.100.9", DashboardKey);

        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, someoneElse.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("a-wrong-key-of-the-right-length-0123456789")]
    [InlineData("local-test-dashboard-key-0123456789abcdeF")]
    public async Task Without_the_right_key_a_named_address_is_ignored(string? key)
    {
        await using var factory = Limited(builder =>
        {
            builder.UseSetting("Dashboard:Key", DashboardKey);
            builder.ConfigureLogging(logging => logging.AddFakeLogging());
        });
        using var client = factory.CreateClient();

        // Three addresses, one connection: without the key they share the connection's limit.
        using var first = await LogInAsync(client, "203.0.113.1", key);
        using var second = await LogInAsync(client, "203.0.113.2", key);
        using var third = await LogInAsync(client, "203.0.113.3", key);

        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        var records = factory.Services.GetFakeLogCollector().GetSnapshot();
        Assert.Contains(records, record =>
            record.Level == LogLevel.Warning && record.Message.Contains("without the dashboard's key", StringComparison.Ordinal));
        // Neither key, the right one or the one sent, reaches a log line.
        Assert.DoesNotContain(records, record =>
            record.Message.Contains(DashboardKey, StringComparison.Ordinal)
            || (key is not null && record.Message.Contains(key, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task With_no_key_configured_no_address_can_be_named()
    {
        await using var factory = Limited(_ => { });
        using var client = factory.CreateClient();

        using var first = await LogInAsync(client, "203.0.113.1", DashboardKey);
        using var second = await LogInAsync(client, "203.0.113.2", DashboardKey);
        using var third = await LogInAsync(client, "203.0.113.3", DashboardKey);

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("203.0.113.7, 198.51.100.9")]
    [InlineData("")]
    public async Task An_address_that_does_not_parse_falls_back_to_the_connection(string address)
    {
        await using var factory = Limited(builder => builder.UseSetting("Dashboard:Key", DashboardKey));
        using var client = factory.CreateClient();

        using var first = await LogInAsync(client, "203.0.113.50", DashboardKey);
        using var second = await LogInAsync(client, address, DashboardKey);
        using var third = await LogInAsync(client, address, DashboardKey);
        using var fourth = await LogInAsync(client, address, DashboardKey);

        // The first had its own limit; the unparsed ones share the connection's.
        Assert.Equal(HttpStatusCode.Unauthorized, third.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, fourth.StatusCode);
    }

    [Fact]
    public async Task Ipv6_addresses_in_one_64_share_a_limit()
    {
        await using var factory = Limited(builder => builder.UseSetting("Dashboard:Key", DashboardKey));
        using var client = factory.CreateClient();

        using var first = await LogInAsync(client, "2001:db8:1:2::1", DashboardKey);
        using var second = await LogInAsync(client, "2001:db8:1:2:aaaa::2", DashboardKey);
        using var third = await LogInAsync(client, "2001:db8:1:2::3", DashboardKey);
        using var nextBlock = await LogInAsync(client, "2001:db8:1:3::1", DashboardKey);
        using var mapped = await LogInAsync(client, "::ffff:203.0.113.7", DashboardKey);
        using var plain = await LogInAsync(client, "203.0.113.7", DashboardKey);
        using var plainAgain = await LogInAsync(client, "203.0.113.7", DashboardKey);

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, nextBlock.StatusCode);
        // An IPv4 address written as IPv6 is the same address.
        Assert.Equal(HttpStatusCode.Unauthorized, plain.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, plainAgain.StatusCode);
    }

    [Fact]
    public async Task A_short_key_stops_startup()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration["Jwt:SigningKey"] = Convert.ToBase64String(ApiFactory.SigningKey);
        builder.Configuration["Dashboard:Key"] = "too-short";
        builder.AddApiAuthentication();
        await using var app = builder.Build();

        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => app.StartAsync(Cancellation));

        Assert.Contains("Key", error.Message, StringComparison.Ordinal);
    }

    /// <summary>The API with a per-address limit of two, so a third login from one address is refused.</summary>
    private WebApplicationFactory<Program> Limited(Action<IWebHostBuilder> configure) =>
        Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Auth:LoginRateLimit:PerAddressPermits", "2");
            configure(builder);
        });

    private static async Task<HttpResponseMessage> LogInAsync(HttpClient client, string address, string? key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, LoginPath)
        {
            Content = JsonContent.Create(new { email = $"someone-{Guid.NewGuid():N}@example.com", password = "x" }),
        };
        request.Headers.Add(ClientAddress.AddressHeader, address);
        if (key is not null)
        {
            request.Headers.Add(ClientAddress.KeyHeader, key);
        }

        return await client.SendAsync(request, Cancellation);
    }
}
