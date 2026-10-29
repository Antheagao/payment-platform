using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace PaymentPlatform.Api.Auth;

/// <summary>
/// Whose request this is, for the per-address login limit (ADR 010): the person's address as the dashboard names it
/// in <c>X-Client-Address</c> under its key, or the connection's otherwise. A named address without the key, with
/// a wrong one, or that does not parse is ignored, so no caller chooses its own limit.
/// </summary>
public static partial class ClientAddress
{
    public const string AddressHeader = "X-Client-Address";
    public const string KeyHeader = "X-Dashboard-Key";

    /// <summary>
    /// The partition the address falls in: an IPv4 address as itself, an IPv6 one by its <c>/64</c>, the block one
    /// subscriber is usually given, so a single person cannot take a fresh limit with each of their addresses.
    /// </summary>
    public static string PartitionOf(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var address = Named(context) ?? context.Connection.RemoteIpAddress;
        return address is null ? "unknown" : Partition(address);
    }

    private static IPAddress? Named(HttpContext context)
    {
        var headers = context.Request.Headers;
        if (!headers.ContainsKey(AddressHeader))
        {
            return null;
        }

        var key = context.RequestServices.GetRequiredService<IOptions<DashboardOptions>>().Value.Key;
        if (key is null || !IsKey(headers[KeyHeader], key))
        {
            // Most likely a dashboard whose key no longer matches the API's: its users then share one limit.
            LogUntrustedAddress(context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ClientAddress)));
            return null;
        }

        return headers[AddressHeader].Count == 1 && IPAddress.TryParse(headers[AddressHeader][0], out var named) ? named : null;
    }

    private static bool IsKey(StringValues sent, string key) =>
        sent.Count == 1
        // Compared as hashes, so the time taken says nothing about how much of the key was right, or its length.
        && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(sent[0] ?? string.Empty)),
            SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private static string Partition(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return $"{new IPAddress(bytes)}/64";
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A login named its client's address without the dashboard's key, so it is limited by its connection instead")]
    private static partial void LogUntrustedAddress(ILogger logger);
}
