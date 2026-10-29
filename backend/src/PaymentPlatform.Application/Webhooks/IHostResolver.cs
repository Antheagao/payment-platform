using System.Net;

namespace PaymentPlatform.Application.Webhooks;

/// <summary>Resolves a host name to its addresses; a port so tests can say what a name resolves to.</summary>
public interface IHostResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

/// <summary>The system resolver.</summary>
public sealed class DnsHostResolver : IHostResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}
