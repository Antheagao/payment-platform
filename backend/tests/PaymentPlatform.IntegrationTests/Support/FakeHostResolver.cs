using System.Net;
using System.Net.Sockets;
using PaymentPlatform.Application.Webhooks;

namespace PaymentPlatform.IntegrationTests.Support;

/// <summary>Answers for a few names, so webhook tests never depend on real DNS; any other name does not resolve.</summary>
public sealed class FakeHostResolver : IHostResolver
{
    public const string PublicHost = "hooks.example.com";
    public const string PrivateHost = "internal.example.com";

    private static readonly Dictionary<string, IPAddress[]> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        [PublicHost] = [IPAddress.Parse("93.184.215.14")],
        [PrivateHost] = [IPAddress.Parse("10.0.0.5")],
        ["localhost"] = [IPAddress.Loopback],
    };

    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Names.TryGetValue(host, out var addresses)
            ? Task.FromResult(addresses)
            : Task.FromException<IPAddress[]>(new SocketException((int)SocketError.HostNotFound));
}
