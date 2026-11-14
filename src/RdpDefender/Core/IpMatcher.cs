using System.Net;
using System.Net.Sockets;

namespace RdpDefender.Core;

public sealed class IpNetwork
{
    public IPAddress Network { get; }
    public int PrefixLength { get; }

    public IpNetwork(IPAddress network, int prefixLength)
    {
        Network = IpMatcher.Normalize(network);
        PrefixLength = prefixLength;
    }

    public bool Contains(IPAddress address)
    {
        var candidate = IpMatcher.Normalize(address);
        if (candidate.AddressFamily != Network.AddressFamily) return false;

        var candidateBytes = candidate.GetAddressBytes();
        var networkBytes = Network.GetAddressBytes();
        if (candidateBytes.Length != networkBytes.Length) return false;

        var fullBytes = PrefixLength / 8;
        var remainingBits = PrefixLength % 8;

        for (var i = 0; i < fullBytes; i++)
        {
            if (candidateBytes[i] != networkBytes[i]) return false;
        }

        if (remainingBits > 0 && fullBytes < candidateBytes.Length)
        {
            var mask = (byte)(0xFF << (8 - remainingBits));
            if ((candidateBytes[fullBytes] & mask) != (networkBytes[fullBytes] & mask))
                return false;
        }

        return true;
    }

    public override string ToString() => $"{Network}/{PrefixLength}";
}

public static class IpMatcher
{
    public static IPAddress Normalize(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv4MappedToIPv6)
            return address.MapToIPv4();
        return address;
    }

    public static bool TryParseAddress(string? text, out IPAddress? address)
    {
        address = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!IPAddress.TryParse(text.Trim(), out var parsed)) return false;
        address = Normalize(parsed);
        return true;
    }

    public static bool TryParseNetwork(string? text, out IpNetwork? network, out string? error)
    {
        network = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "пустое значение";
            return false;
        }

        var parts = text.Trim().Split('/');
        if (parts.Length > 2)
        {
            error = "ожидается адрес или подсеть вида 10.0.0.0/8";
            return false;
        }

        if (!IPAddress.TryParse(parts[0].Trim(), out var address))
        {
            error = "некорректный IP-адрес";
            return false;
        }

        address = Normalize(address);
        var maxPrefix = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        var prefix = maxPrefix;

        if (parts.Length == 2)
        {
            var prefixText = parts[1].Trim();
            if (!int.TryParse(prefixText, out prefix) || prefix < 0 || prefix > maxPrefix)
            {
                error = $"длина маски должна быть от 0 до {maxPrefix}";
                return false;
            }
        }

        network = new IpNetwork(address, prefix);
        return true;
    }

    public static bool IsMatch(IPAddress address, IEnumerable<string> entries, out string? matched)
    {
        matched = null;
        var normalized = Normalize(address);

        foreach (var entry in entries)
        {
            if (!TryParseNetwork(entry, out var network, out _) || network is null)
                continue;

            if (network.Contains(normalized))
            {
                matched = entry;
                return true;
            }
        }

        return false;
    }

    public static bool IsLoopback(IPAddress address) => IPAddress.IsLoopback(Normalize(address));
}
