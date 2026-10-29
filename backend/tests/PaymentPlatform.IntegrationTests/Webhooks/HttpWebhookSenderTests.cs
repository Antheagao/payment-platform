using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PaymentPlatform.Application;
using PaymentPlatform.Application.Webhooks;

namespace PaymentPlatform.IntegrationTests.Webhooks;

/// <summary>
/// The real HTTP sender against a receiver on loopback: what a merchant's server gets, and the connect-time SSRF
/// guard, which refuses a name or address that is not public unless it is listed as a local receiver.
/// </summary>
public sealed class HttpWebhookSenderTests : IAsyncLifetime
{
    private const string Body = """{"id":"evt_1","type":"payment.captured","data":{"amount_minor":2599}}""";

    private readonly ConcurrentQueue<(Dictionary<string, string> Headers, string Body)> _received = new();
    private WebApplication? _receiver;
    private int _port;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _receiver = builder.Build();
        _receiver.MapPost("/hooks", async (HttpRequest request) =>
        {
            // Copied: Kestrel reuses the request's header collection once the request ends.
            using var reader = new StreamReader(request.Body);
            var headers = request.Headers.ToDictionary(header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase);
            _received.Enqueue((headers, await reader.ReadToEndAsync()));
            return Results.NoContent();
        });
        _receiver.MapPost("/moved", () => Results.Redirect("/hooks"));
        await _receiver.StartAsync(Cancellation);
        _port = new Uri(_receiver.Urls.Single()).Port;
    }

    public async ValueTask DisposeAsync()
    {
        if (_receiver is not null)
        {
            await _receiver.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_listed_receiver_gets_the_body_the_signature_headers_and_the_trace()
    {
        using var services = Services(localReceivers: ["127.0.0.1"]);
        using var request = new Activity("webhook deliver").SetIdFormat(ActivityIdFormat.W3C).Start();

        var attempt = await services.GetRequiredService<IWebhookSender>().SendAsync(
            new WebhookRequest(new Uri($"http://127.0.0.1:{_port}/hooks"), "evt_1", 1793260000, Body, "v1,c2lnbmF0dXJl", "demo-1"), Cancellation);

        Assert.Equal(new WebhookAttempt(204, null), attempt);
        var (headers, body) = Assert.Single(_received);
        Assert.Equal(Body, body);
        Assert.Equal("evt_1", headers["webhook-id"]);
        Assert.Equal("1793260000", headers["webhook-timestamp"]);
        Assert.Equal("v1,c2lnbmF0dXJl", headers["webhook-signature"]);
        Assert.Equal("demo-1", headers["X-Correlation-Id"]);
        Assert.StartsWith("application/json", headers["Content-Type"], StringComparison.Ordinal);
        Assert.StartsWith("PaymentPlatform-Webhooks/", headers["User-Agent"], StringComparison.Ordinal);
        Assert.Contains(request.TraceId.ToHexString(), headers["traceparent"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("rebound.example.com")]
    public async Task A_host_that_is_not_public_and_not_listed_is_refused_before_any_connection(string host)
    {
        // rebound.example.com passed the check when it was set and now resolves to loopback: DNS rebinding.
        using var services = Services(localReceivers: []);

        var attempt = await services.GetRequiredService<IWebhookSender>().SendAsync(
            new WebhookRequest(new Uri($"http://{host}:{_port}/hooks"), "evt_1", 1793260000, Body, "v1,c2lnbmF0dXJl", null), Cancellation);

        Assert.Null(attempt.StatusCode);
        Assert.Equal($"{host} does not resolve only to public addresses", attempt.Error);
        Assert.Empty(_received);
    }

    [Fact]
    public async Task A_redirect_is_not_followed()
    {
        using var services = Services(localReceivers: ["127.0.0.1"]);

        var attempt = await services.GetRequiredService<IWebhookSender>().SendAsync(
            new WebhookRequest(new Uri($"http://127.0.0.1:{_port}/moved"), "evt_1", 1793260000, Body, "v1,c2lnbmF0dXJl", null), Cancellation);

        Assert.Equal(new WebhookAttempt(302, "HTTP 302"), attempt);
        Assert.False(attempt.Delivered);
        Assert.Empty(_received);
    }

    [Fact]
    public async Task A_receiver_that_is_not_listening_is_a_failed_connection()
    {
        using var services = Services(localReceivers: ["127.0.0.1"]);
        await _receiver!.StopAsync(Cancellation);

        var attempt = await services.GetRequiredService<IWebhookSender>().SendAsync(
            new WebhookRequest(new Uri($"http://127.0.0.1:{_port}/hooks"), "evt_1", 1793260000, Body, "v1,c2lnbmF0dXJl", null), Cancellation);

        Assert.Equal(new WebhookAttempt(null, "the connection failed"), attempt);
    }

    private static ServiceProvider Services(string[] localReceivers)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(localReceivers.Select((host, index) => new KeyValuePair<string, string?>($"Webhooks:LocalReceiverHosts:{index}", host)))
            .Build();
        return new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging()
            .AddApplication()
            .AddSingleton<IHostResolver>(new ReboundResolver())
            .BuildServiceProvider();
    }

    private sealed class ReboundResolver : IHostResolver
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult(new[] { IPAddress.Loopback });
    }
}
