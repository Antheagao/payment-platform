using System.Net;
using PaymentPlatform.Application.Webhooks;

namespace PaymentPlatform.Application.Tests.Webhooks;

public sealed class WebhookAddressesTests
{
    [Theory]
    [InlineData("93.184.215.14")]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.1")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.1")]
    [InlineData("169.253.255.255")]
    [InlineData("11.0.0.1")]
    [InlineData("223.255.255.254")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2a00:1450:4001:82b::200e")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("64:ff9b::808:808")]
    public void A_public_unicast_address_is_allowed(string address)
    {
        Assert.True(WebhookAddresses.IsPublic(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("0.1.2.3")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("100.64.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.254")]
    [InlineData("169.254.169.254")]
    [InlineData("169.254.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.0.0.8")]
    [InlineData("192.0.2.1")]
    [InlineData("192.168.1.1")]
    [InlineData("198.18.0.1")]
    [InlineData("198.51.100.7")]
    [InlineData("203.0.113.9")]
    [InlineData("224.0.0.251")]
    [InlineData("239.255.255.250")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("168.63.129.16")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("::127.0.0.1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.1.2.3")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("64:ff9b::7f00:1")]
    [InlineData("64:ff9b::a9fe:a9fe")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456:789a::1")]
    [InlineData("fe80::1")]
    [InlineData("fec0::1")]
    [InlineData("ff02::1")]
    [InlineData("2001:db8::1")]
    [InlineData("2001::1")]
    [InlineData("2002:c0a8:101::1")]
    [InlineData("100::1")]
    public void A_private_loopback_link_local_reserved_or_multicast_address_is_refused(string address)
    {
        Assert.False(WebhookAddresses.IsPublic(IPAddress.Parse(address)));
    }
}
