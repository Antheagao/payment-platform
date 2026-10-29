using System.Net;
using System.Net.Sockets;

namespace PaymentPlatform.Application.Webhooks;

/// <summary>
/// Which addresses a webhook may be sent to: only public unicast ones. A merchant chooses the URL, so without this
/// check a delivery could reach the platform's own network, a cloud metadata service, or a loopback admin port
/// (SSRF; security overlay).
/// </summary>
public static class WebhookAddresses
{
    // The IANA special-purpose registries (RFC 6890 and its updates), plus Azure's platform address.
    private static readonly IPNetwork[] Refused =
    [
        IPNetwork.Parse("0.0.0.0/8"),
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("100.64.0.0/10"),
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("169.254.0.0/16"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.0.0.0/24"),
        IPNetwork.Parse("192.0.2.0/24"),
        IPNetwork.Parse("192.88.99.0/24"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("198.18.0.0/15"),
        IPNetwork.Parse("198.51.100.0/24"),
        IPNetwork.Parse("203.0.113.0/24"),
        IPNetwork.Parse("224.0.0.0/4"),
        IPNetwork.Parse("240.0.0.0/4"),

        // Azure's virtual public address for DHCP, DNS, and the VM agent: public in form, platform-only in use.
        IPNetwork.Parse("168.63.129.16/32"),

        IPNetwork.Parse("::/128"),
        IPNetwork.Parse("::1/128"),
        IPNetwork.Parse("100::/64"),
        IPNetwork.Parse("2001::/23"),
        IPNetwork.Parse("2001:db8::/32"),
        IPNetwork.Parse("2002::/16"),
        IPNetwork.Parse("fc00::/7"),
        IPNetwork.Parse("fe80::/10"),
        IPNetwork.Parse("fec0::/10"),
        IPNetwork.Parse("ff00::/8"),
    ];

    // IPv6 forms that carry an IPv4 address, judged by the address they carry.
    private static readonly IPNetwork Nat64 = IPNetwork.Parse("64:ff9b::/96");
    private static readonly IPNetwork Compatible = IPNetwork.Parse("::/96");

    /// <summary>True for a public unicast address; false for every private, loopback, link-local, reserved, or multicast one.</summary>
    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            return IsPublic(address.MapToIPv4());
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6 && (Nat64.Contains(address) || Compatible.Contains(address)))
        {
            var bytes = address.GetAddressBytes();
            var embedded = new IPAddress(bytes.AsSpan(12, 4));
            return !IPAddress.Any.Equals(embedded) && IsPublic(embedded);
        }

        return address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6
            && !Refused.Any(network => network.BaseAddress.AddressFamily == address.AddressFamily && network.Contains(address));
    }
}
