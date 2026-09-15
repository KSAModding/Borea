using System.Net;
using System.Net.Sockets;

namespace Borea.Network.Images;

/// <summary>Connects only to an address it checked, and only when every address of the host is public by the ranges of the content index checks.</summary>
internal static class PublicNetworkTarget
{
    private static readonly IPNetwork[] NotPublicV4 =
    [
        IPNetwork.Parse("0.0.0.0/8"),
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("100.64.0.0/10"),
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("169.254.0.0/16"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.0.0.0/24"),
        IPNetwork.Parse("192.0.0.170/31"),
        IPNetwork.Parse("192.0.2.0/24"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("198.18.0.0/15"),
        IPNetwork.Parse("198.51.100.0/24"),
        IPNetwork.Parse("203.0.113.0/24"),
        IPNetwork.Parse("224.0.0.0/4"),
        IPNetwork.Parse("240.0.0.0/4"),
        IPNetwork.Parse("255.255.255.255/32"),
    ];

    private static readonly IPNetwork[] PublicInsideNotPublicV4 =
    [
        IPNetwork.Parse("192.0.0.9/32"),
        IPNetwork.Parse("192.0.0.10/32"),
    ];

    private static readonly IPNetwork[] NotPublicV6 =
    [
        IPNetwork.Parse("::1/128"),
        IPNetwork.Parse("::/128"),
        IPNetwork.Parse("64:ff9b:1::/48"),
        IPNetwork.Parse("100::/64"),
        IPNetwork.Parse("2001::/23"),
        IPNetwork.Parse("2001:db8::/32"),
        IPNetwork.Parse("2002::/16"),
        IPNetwork.Parse("3fff::/20"),
        IPNetwork.Parse("fc00::/7"),
        IPNetwork.Parse("fe80::/10"),
        IPNetwork.Parse("fec0::/10"),
        IPNetwork.Parse("ff00::/8"),
    ];

    private static readonly IPNetwork[] PublicInsideNotPublicV6 =
    [
        IPNetwork.Parse("2001:1::1/128"),
        IPNetwork.Parse("2001:1::2/128"),
        IPNetwork.Parse("2001:3::/32"),
        IPNetwork.Parse("2001:4:112::/48"),
        IPNetwork.Parse("2001:20::/28"),
        IPNetwork.Parse("2001:30::/28"),
    ];

    /// <summary>NAT64, IPv4-compatible and IPv4-translated addresses carry an IPv4 address in their last 32 bits.</summary>
    private static readonly IPNetwork[] EmbedIPv4 =
    [
        IPNetwork.Parse("64:ff9b::/96"),
        IPNetwork.Parse("::/96"),
        IPNetwork.Parse("::ffff:0:0:0/96"),
    ];

    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
            return IsPublic(address.MapToIPv4());

        if (address.AddressFamily == AddressFamily.InterNetwork)
            return !IsInside(address, NotPublicV4, PublicInsideNotPublicV4);

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return false;

        if (EmbedIPv4.Any(network => network.Contains(address))
            && !IsPublic(new IPAddress(address.GetAddressBytes().AsSpan(12, 4))))
            return false;

        return !IsInside(address, NotPublicV6, PublicInsideNotPublicV6);
    }

    public static ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken) =>
        ConnectAsync(context.DnsEndPoint, Dns.GetHostAddressesAsync, ConnectSocketAsync, cancellationToken);

    internal static async ValueTask<Stream> ConnectAsync(
        DnsEndPoint endPoint,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        Func<IPAddress, int, CancellationToken, Task<Stream>> connect,
        CancellationToken cancellationToken)
    {
        var host = endPoint.Host is ['[', .. var literal, ']'] ? literal : endPoint.Host;
        var addresses = await resolve(host, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0)
            throw new SocketException((int)SocketError.HostNotFound);

        foreach (var address in addresses)
        {
            if (!IsPublic(address))
                throw new BlockedNetworkTargetException($"{host} resolves to {address}, which is not a public address.");
        }

        SocketException? failure = null;
        foreach (var address in addresses)
        {
            try
            {
                return await connect(address, endPoint.Port, cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException exception)
            {
                failure = exception;
            }
        }

        throw failure!;
    }

    private static bool IsInside(IPAddress address, IPNetwork[] networks, IPNetwork[] exceptions) =>
        networks.Any(network => network.Contains(address)) && !exceptions.Any(network => network.Contains(address));

    private static async Task<Stream> ConnectSocketAsync(IPAddress address, int port, CancellationToken cancellationToken)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(address, port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

internal sealed class BlockedNetworkTargetException(string message) : Exception(message);
