using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using PaymentPlatform.Application.Webhooks;

namespace PaymentPlatform.Application.Tests.Webhooks;

public sealed class WebhookUrlPolicyTests
{
    private static readonly Dictionary<string, string[]> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hooks.example.com"] = ["93.184.215.14", "2606:2800:21f:cb07:6820:80da:af6b:8b2c"],
        ["xn--bcher-kva.example"] = ["93.184.215.15"],
        ["internal.example.com"] = ["10.0.0.5"],
        ["rebind.example.com"] = ["93.184.215.14", "127.0.0.1"],
        ["metadata.example.com"] = ["169.254.169.254"],
        ["receiver"] = ["172.18.0.9"],
    };

    private readonly CancellationToken _cancellation = TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("https://hooks.example.com/payments")]
    [InlineData("https://hooks.example.com:8443/payments?merchant=7")]
    [InlineData("HTTPS://HOOKS.EXAMPLE.COM/")]
    [InlineData("https://bücher.example/hooks")]
    [InlineData("https://93.184.215.14/hooks")]
    [InlineData("https://[2606:4700:4700::1111]/hooks")]
    public async Task A_https_url_to_public_addresses_is_allowed(string url)
    {
        Assert.Null(await Policy().CheckAsync(url, _cancellation));
    }

    [Theory]
    [InlineData(null, WebhookUrlPolicy.Required)]
    [InlineData("", WebhookUrlPolicy.Required)]
    [InlineData("   ", WebhookUrlPolicy.Required)]
    [InlineData("/payments", WebhookUrlPolicy.Invalid)]
    [InlineData("hooks.example.com/payments", WebhookUrlPolicy.Invalid)]
    [InlineData("ftp://hooks.example.com/payments", WebhookUrlPolicy.Invalid)]
    [InlineData("file:///etc/passwd", WebhookUrlPolicy.Invalid)]
    [InlineData("https://user:pass@hooks.example.com/payments", WebhookUrlPolicy.Invalid)]
    [InlineData("https://hooks.example.com/payments#part", WebhookUrlPolicy.Invalid)]
    [InlineData("http://hooks.example.com/payments", WebhookUrlPolicy.NotHttps)]
    [InlineData("https://127.0.0.1/hooks", WebhookUrlPolicy.NotPublic)]
    [InlineData("https://2130706433/hooks", WebhookUrlPolicy.NotPublic)]
    [InlineData("https://0x7f.1/hooks", WebhookUrlPolicy.NotPublic)]
    [InlineData("https://10.1.2.3/hooks", WebhookUrlPolicy.NotPublic)]
    [InlineData("https://169.254.169.254/latest/meta-data", WebhookUrlPolicy.NotPublic)]
    [InlineData("https://[::1]/hooks", WebhookUrlPolicy.NotPublic)]
    [InlineData("https://[::ffff:127.0.0.1]/hooks", WebhookUrlPolicy.NotPublic)]
    [InlineData("https://[fd00::1]/hooks", WebhookUrlPolicy.NotPublic)]
    [InlineData("https://internal.example.com/hooks", WebhookUrlPolicy.NotPublic)]
    [InlineData("https://metadata.example.com/hooks", WebhookUrlPolicy.NotPublic)]
    [InlineData("https://rebind.example.com/hooks", WebhookUrlPolicy.NotPublic)]
    [InlineData("https://localhost/hooks", WebhookUrlPolicy.NotPublic)]
    [InlineData("https://nowhere.example.com/hooks", WebhookUrlPolicy.Unresolvable)]
    [InlineData("http://receiver/hooks", WebhookUrlPolicy.NotHttps)]
    [InlineData("https://receiver/hooks", WebhookUrlPolicy.NotPublic)]
    public async Task A_url_that_breaks_a_rule_gets_its_code(string? url, string code)
    {
        Assert.Equal(code, await Policy().CheckAsync(url, _cancellation));
    }

    [Fact]
    public async Task A_url_over_2048_characters_is_too_long()
    {
        const string start = "https://hooks.example.com/";
        var url = start + new string('a', 2048 - start.Length + 1);

        Assert.Equal(WebhookUrlPolicy.TooLong, await Policy().CheckAsync(url, _cancellation));
        Assert.Null(await Policy().CheckAsync(url[..^1], _cancellation));
    }

    [Theory]
    [InlineData("http://receiver/hooks")]
    [InlineData("https://RECEIVER:8080/hooks")]
    public async Task A_listed_local_receiver_may_use_http_and_a_private_address(string url)
    {
        Assert.Null(await Policy(localReceivers: ["receiver"]).CheckAsync(url, _cancellation));
    }

    [Fact]
    public async Task The_local_receiver_list_frees_only_the_names_it_lists()
    {
        var policy = Policy(localReceivers: ["receiver"]);

        Assert.Equal(WebhookUrlPolicy.NotPublic, await policy.CheckAsync("https://internal.example.com/hooks", _cancellation));
        Assert.Equal(WebhookUrlPolicy.NotHttps, await policy.CheckAsync("http://hooks.example.com/hooks", _cancellation));
    }

    [Fact]
    public async Task A_resolver_that_does_not_answer_in_time_makes_the_url_unresolvable()
    {
        var policy = new WebhookUrlPolicy(new SilentResolver(), Options.Create(new WebhookOptions()));

        Assert.Equal(WebhookUrlPolicy.Unresolvable, await policy.CheckAsync("https://slow.example.com/hooks", _cancellation));
    }

    [Theory]
    [InlineData("hooks.example.com", 2)]
    [InlineData("rebind.example.com", 0)]
    [InlineData("internal.example.com", 0)]
    [InlineData("nowhere.example.com", 0)]
    [InlineData("127.0.0.1", 0)]
    [InlineData("93.184.215.14", 1)]
    public async Task A_connection_may_use_the_addresses_of_a_name_only_when_all_of_them_are_public(string host, int allowed)
    {
        Assert.Equal(allowed, (await Policy().ResolveAllowedAsync(host, _cancellation)).Length);
    }

    [Fact]
    public async Task A_listed_local_receiver_connects_to_its_private_address()
    {
        var addresses = await Policy(localReceivers: ["receiver"]).ResolveAllowedAsync("receiver", _cancellation);

        Assert.Equal([IPAddress.Parse("172.18.0.9")], addresses);
    }

    private static WebhookUrlPolicy Policy(string[]? localReceivers = null) =>
        new(new FakeResolver(), Options.Create(new WebhookOptions { LocalReceiverHosts = localReceivers ?? [] }));

    private sealed class FakeResolver : IHostResolver
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
            host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ? Task.FromResult(new[] { IPAddress.Loopback, IPAddress.IPv6Loopback })
            : Names.TryGetValue(host, out var addresses) ? Task.FromResult(addresses.Select(IPAddress.Parse).ToArray())
            : Task.FromException<IPAddress[]>(new SocketException((int)SocketError.HostNotFound));
    }

    private sealed class SilentResolver : IHostResolver
    {
        public async Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return [];
        }
    }
}
