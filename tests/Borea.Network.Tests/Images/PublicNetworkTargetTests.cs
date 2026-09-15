using System.Net;
using System.Net.Sockets;
using Borea.Network.Images;

namespace Borea.Network.Tests.Images;

public sealed class PublicNetworkTargetTests
{
    private const string Public = "93.184.216.34";

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("10.1.2.3")]
    [InlineData("100.100.100.200")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("172.16.0.1")]
    [InlineData("192.0.0.1")]
    [InlineData("192.0.0.170")]
    [InlineData("192.0.2.1")]
    [InlineData("192.168.1.1")]
    [InlineData("198.18.0.1")]
    [InlineData("198.51.100.1")]
    [InlineData("203.0.113.1")]
    [InlineData("224.0.0.1")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.1.2.3")]
    [InlineData("64:ff9b::7f00:1")]
    [InlineData("64:ff9b:1::1")]
    [InlineData("::7f00:1")]
    [InlineData("::ffff:0:7f00:1")]
    [InlineData("100::1")]
    [InlineData("2001::1")]
    [InlineData("2001:db8::1")]
    [InlineData("2002:7f00:1::")]
    [InlineData("3fff::1")]
    [InlineData("fd00:ec2::254")]
    [InlineData("fe80::1")]
    [InlineData("fec0::1")]
    [InlineData("ff02::1")]
    public void IsPublic_AddressInABlockedRange_IsFalse(string address)
    {
        Assert.False(PublicNetworkTarget.IsPublic(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData(Public)]
    [InlineData("192.0.0.9")]
    [InlineData("192.0.0.10")]
    [InlineData("2001:1::1")]
    [InlineData("2001:1::2")]
    [InlineData("2001:3::1")]
    [InlineData("2001:4:112::1")]
    [InlineData("2001:20::1")]
    [InlineData("2001:30::1")]
    [InlineData("2606:4700::1111")]
    [InlineData("::ffff:93.184.216.34")]
    [InlineData("64:ff9b::5db8:d822")]
    public void IsPublic_PublicAddress_IsTrue(string address)
    {
        Assert.True(PublicNetworkTarget.IsPublic(IPAddress.Parse(address)));
    }

    [Fact]
    public async Task ConnectAsync_PublicHost_ConnectsToTheAddressThatWasChecked()
    {
        string? resolved = null;
        var connected = new List<(IPAddress Address, int Port)>();

        await using var stream = await PublicNetworkTarget.ConnectAsync(
            new DnsEndPoint("images.example", 8443),
            (host, _) =>
            {
                resolved = host;
                return Task.FromResult(new[] { IPAddress.Parse(Public) });
            },
            (address, port, _) =>
            {
                connected.Add((address, port));
                return Task.FromResult<Stream>(new MemoryStream());
            },
            CancellationToken.None);

        Assert.Equal("images.example", resolved);
        Assert.Equal((IPAddress.Parse(Public), 8443), Assert.Single(connected));
    }

    [Fact]
    public async Task ConnectAsync_OnePrivateAmongPublicAddresses_ConnectsToNone()
    {
        var connected = new List<IPAddress>();

        await Assert.ThrowsAsync<BlockedNetworkTargetException>(async () => await PublicNetworkTarget.ConnectAsync(
            new DnsEndPoint("images.example", 443),
            (_, _) => Task.FromResult(new[] { IPAddress.Parse(Public), IPAddress.Parse("10.0.0.1") }),
            (address, _, _) =>
            {
                connected.Add(address);
                return Task.FromResult<Stream>(new MemoryStream());
            },
            CancellationToken.None));

        Assert.Empty(connected);
    }

    [Fact]
    public async Task ConnectAsync_BracketedIPv6Literal_ChecksTheAddress()
    {
        await Assert.ThrowsAsync<BlockedNetworkTargetException>(async () => await PublicNetworkTarget.ConnectAsync(
            new DnsEndPoint("[::1]", 443),
            (host, _) => Task.FromResult(new[] { IPAddress.Parse(host) }),
            (_, _, _) => Task.FromResult<Stream>(new MemoryStream()),
            CancellationToken.None));
    }

    [Fact]
    public async Task ConnectAsync_FirstAddressRefuses_ConnectsToTheNext()
    {
        var other = IPAddress.Parse("93.184.216.35");
        var connected = new List<IPAddress>();

        await using var stream = await PublicNetworkTarget.ConnectAsync(
            new DnsEndPoint("images.example", 443),
            (_, _) => Task.FromResult(new[] { IPAddress.Parse(Public), other }),
            (address, _, _) =>
            {
                connected.Add(address);
                return address.Equals(other)
                    ? Task.FromResult<Stream>(new MemoryStream())
                    : Task.FromException<Stream>(new SocketException((int)SocketError.ConnectionRefused));
            },
            CancellationToken.None);

        Assert.Equal(new[] { IPAddress.Parse(Public), other }, connected);
    }
}
