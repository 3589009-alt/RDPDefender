using System.Net;

namespace RdpDefender.Core;

public readonly record struct TcpConnectionSnapshot(
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    int RemotePort,
    string State);

/// <summary>Выделяет новые входящие TCP-подключения к заданному локальному порту.</summary>
public sealed class TcpConnectionTracker
{
    private readonly HashSet<string> _active = new(StringComparer.OrdinalIgnoreCase);

    public int Port { get; set; } = 3389;

    public IReadOnlyList<string> Observe(IEnumerable<TcpConnectionSnapshot> connections)
    {
        var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fresh = new List<string>();

        foreach (var connection in connections)
        {
            if (connection.LocalPort != Port || connection.RemotePort <= 0)
                continue;

            if (!IPAddress.TryParse(connection.RemoteAddress, out var remote) || remote is null)
                continue;

            if (IPAddress.IsLoopback(remote) ||
                remote.Equals(IPAddress.Any) ||
                remote.Equals(IPAddress.IPv6Any))
            {
                continue;
            }

            if (!string.Equals(connection.State, "Established", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(connection.State, "SynReceived", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = $"{connection.LocalAddress}|{connection.LocalPort}|{connection.RemoteAddress}|{connection.RemotePort}";
            current.Add(key);

            if (_active.Add(key))
                fresh.Add(IpMatcher.Normalize(remote).ToString());
        }

        _active.IntersectWith(current);
        return fresh;
    }

    public void Clear() => _active.Clear();
}
