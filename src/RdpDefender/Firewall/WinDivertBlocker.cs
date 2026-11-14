using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using RdpDefender.Core;
using RdpDefender.Infrastructure;

namespace RdpDefender.Firewall;

/// <summary>
/// Блокировка на уровне WFP через WinDivert: пакеты от заблокированных
/// адресов перехватываются и не возвращаются в сетевой стек.
/// </summary>
public sealed class WinDivertBlocker : IFirewall
{
    private const int MaxIpsPerSession = 200;
    private const int PacketBufferSize = 40 + 0xFFFF;

    private readonly Logger _logger;
    private readonly object _sync = new();
    private readonly HashSet<string> _ips = new(StringComparer.OrdinalIgnoreCase);
    private List<DivertSession> _sessions = new();
    private bool _allTraffic = true;
    private int _port = 3389;
    private bool _available;
    private bool _disposed;

    public string Name => "WinDivert 2.2.2 (WFP)";

    public bool IsAvailable
    {
        get
        {
            lock (_sync)
                return _available && _sessions.All(session => session.IsAlive);
        }
    }

    public WinDivertBlocker(Logger logger)
    {
        _logger = logger;
        WinDivertRuntime.EnsureExtracted(logger);

        var probeFilter = "inbound and ip.SrcAddr == 192.0.2.254";
        var filterError = WinDivertRuntime.ValidateFilter(probeFilter);
        if (filterError is not null)
            throw new InvalidOperationException("WinDivert не принял тестовый фильтр: " + filterError);

        var handle = WinDivertNative.WinDivertOpen(probeFilter, WinDivertNative.LayerNetwork, 0, 0);
        if (handle == IntPtr.Zero || handle == WinDivertNative.InvalidHandle)
        {
            var error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, $"WinDivertOpen: {new Win32Exception(error).Message}");
        }

        WinDivertNative.WinDivertClose(handle);
        _available = true;
        _logger.Info("Блокировка: WinDivert 2.2.2, драйвер WFP доступен.");
    }

    public bool AddBlock(string ip, string ruleName, string description, bool allTraffic, int port)
    {
        if (!TryNormalize(ip, out var normalized)) return false;

        lock (_sync)
        {
            if (_disposed) return false;
            _allTraffic = allTraffic;
            _port = Math.Clamp(port, 1, 65_535);
            _ips.Add(normalized);
            return RebuildLocked(keepOldOnFailure: true);
        }
    }

    public bool RemoveBlock(string ip)
    {
        if (!TryNormalize(ip, out var normalized)) return true;

        lock (_sync)
        {
            if (_disposed) return true;
            if (!_ips.Remove(normalized)) return true;
            return RebuildLocked(keepOldOnFailure: false);
        }
    }

    public bool RuleExists(string ip) => TryNormalize(ip, out var normalized) && _ips.Contains(normalized);

    public bool ClearGroup()
    {
        lock (_sync)
        {
            _ips.Clear();
            CloseSessionsLocked();
            _available = true;
            return true;
        }
    }

    public bool Shutdown() => ClearGroup();

    private bool RebuildLocked(bool keepOldOnFailure)
    {
        if (_ips.Count == 0)
        {
            CloseSessionsLocked();
            _available = true;
            return true;
        }

        var newSessions = new List<DivertSession>();
        try
        {
            foreach (var chunk in _ips
                         .OrderBy(ip => ip, StringComparer.OrdinalIgnoreCase)
                         .Chunk(MaxIpsPerSession))
            {
                var filter = BuildFilter(chunk, _allTraffic, _port);
                newSessions.Add(DivertSession.Open(filter, _logger));
            }
        }
        catch (Exception ex)
        {
            foreach (var session in newSessions)
                session.Dispose();

            _available = false;
            _logger.Error("WinDivert: не удалось применить фильтр блокировки: " + ex.Message);

            if (!keepOldOnFailure)
                CloseSessionsLocked();

            return false;
        }

        var oldSessions = _sessions;
        _sessions = newSessions;
        _available = true;

        foreach (var session in oldSessions)
            session.Dispose();

        return true;
    }

    private void CloseSessionsLocked()
    {
        var sessions = _sessions;
        _sessions = new List<DivertSession>();
        foreach (var session in sessions)
            session.Dispose();
    }

    internal static string BuildFilter(IReadOnlyCollection<string> ips, bool allTraffic, int port)
    {
        var conditions = new List<string>(ips.Count);

        foreach (var raw in ips)
        {
            if (!IPAddress.TryParse(raw, out var parsed)) continue;
            var address = IpMatcher.Normalize(parsed);
            conditions.Add(address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                ? $"ipv6.SrcAddr == {address}"
                : $"ip.SrcAddr == {address}");
        }

        if (conditions.Count == 0)
            throw new InvalidOperationException("Нет корректных адресов для фильтра WinDivert.");

        var sourceList = string.Join(" or ", conditions);
        var scope = allTraffic
            ? "inbound"
            : $"inbound and (tcp.DstPort == {Math.Clamp(port, 1, 65_535)} or udp.DstPort == {Math.Clamp(port, 1, 65_535)})";

        return $"{scope} and ({sourceList})";
    }

    private static bool TryNormalize(string ip, out string normalized)
    {
        normalized = "";
        if (!IPAddress.TryParse(ip, out var address)) return false;
        normalized = IpMatcher.Normalize(address).ToString();
        return true;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            CloseSessionsLocked();
        }
    }

    private sealed class DivertSession : IDisposable
    {
        private readonly IntPtr _handle;
        private readonly Logger _logger;
        private readonly string _filter;
        private readonly Thread _thread;
        private volatile bool _running = true;
        private volatile bool _failed;

        public bool IsAlive => _running && !_failed && _thread.IsAlive;

        public static DivertSession Open(string filter, Logger logger)
        {
            var handle = WinDivertNative.WinDivertOpen(filter, WinDivertNative.LayerNetwork, 0, 0);
            if (handle == IntPtr.Zero || handle == WinDivertNative.InvalidHandle)
            {
                var error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error, $"WinDivertOpen: {new Win32Exception(error).Message}");
            }

            WinDivertNative.WinDivertSetParam(handle, WinDivertNative.ParamQueueLength, 8192);
            WinDivertNative.WinDivertSetParam(handle, WinDivertNative.ParamQueueTime, 2000);
            return new DivertSession(handle, filter, logger);
        }

        private DivertSession(IntPtr handle, string filter, Logger logger)
        {
            _handle = handle;
            _filter = filter;
            _logger = logger;
            _thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = "WinDivert drop"
            };
            _thread.Start();
        }

        private void Loop()
        {
            var buffer = new byte[PacketBufferSize];
            var address = Marshal.AllocHGlobal(128);

            try
            {
                while (_running)
                {
                    if (WinDivertNative.WinDivertRecv(
                            _handle, buffer, (uint)buffer.Length, out _, address))
                    {
                        // Пакет намеренно не возвращается в стек — это и есть блокировка.
                        continue;
                    }

                    var error = Marshal.GetLastWin32Error();
                    if (!_running || error is 6 or 995)
                        break;

                    if (error == 232)
                        continue;

                    _failed = true;
                    _logger.Error($"WinDivert: ошибка чтения пакетов ({error}). Фильтр: {_filter}");
                    break;
                }
            }
            catch (Exception ex)
            {
                _failed = true;
                _logger.Error("WinDivert: исключение в цикле блокировки: " + ex.Message);
            }
            finally
            {
                Marshal.FreeHGlobal(address);
            }
        }

        public void Dispose()
        {
            _running = false;
            try { WinDivertNative.WinDivertShutdown(_handle, WinDivertNative.ShutdownRecv); } catch { }
            try { WinDivertNative.WinDivertClose(_handle); } catch { }
            try { _thread.Join(1500); } catch { }
        }
    }
}
