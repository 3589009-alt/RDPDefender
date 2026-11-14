using System.Diagnostics.Eventing.Reader;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RdpDefender.Infrastructure;

namespace RdpDefender.Core;

public interface IRdp261Monitor : IDisposable
{
    string Name { get; }
    int Port { get; set; }
    bool IsRunning { get; }
    string? LastError { get; }
    event EventHandler? ConnectionObserved;
    event EventHandler<string>? SourceObserved;
    event EventHandler<string>? Authenticated;
    void Start();
    void Stop();
}

/// <summary>
/// Подписка на журнал RemoteConnectionManager:
/// 261 — прослушиватель RDP получил соединение (без IP),
/// 1149 — успешная аутентификация (с IP).
/// </summary>
public sealed class Rdp261Monitor : IRdp261Monitor
{
    private const string LogName = "Microsoft-Windows-TerminalServices-RemoteConnectionManager/Operational";
    private const string Query261 = "*[System[(EventID=261)]]";
    private const string Query1149 = "*[System[(EventID=1149)]]";

    private readonly Logger _logger;
    private readonly object _sync = new();
    private readonly System.Threading.Timer _retryTimer;
    private readonly System.Threading.Timer _tcpTimer;
    private readonly TcpConnectionTracker _tcpTracker = new();
    private EventLogWatcher? _watcher261;
    private EventLogWatcher? _watcher1149;
    private bool _running;
    private bool _stopRequested;
    private bool _disposed;
    private string? _lastError;
    private int _startAttempts;

    public string Name => "RDP-подключения · событие 261";
    public bool IsRunning { get { lock (_sync) return _running; } }
    public string? LastError { get { lock (_sync) return _lastError; } }
    public int Port
    {
        get => _tcpTracker.Port;
        set
        {
            lock (_sync)
            {
                if (_tcpTracker.Port == value) return;
                _tcpTracker.Port = value;
                _tcpTracker.Clear();
            }
        }
    }

    public event EventHandler? ConnectionObserved;
    public event EventHandler<string>? SourceObserved;
    public event EventHandler<string>? Authenticated;

    public Rdp261Monitor(Logger logger)
    {
        _logger = logger;
        _retryTimer = new System.Threading.Timer(_ => RetryTick(), null, Timeout.Infinite, Timeout.Infinite);
        _tcpTimer = new System.Threading.Timer(_ => PollTcp(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _stopRequested = false;
            TryStartLocked();
        }
    }

    public void Stop()
    {
        EventLogWatcher? first;
        EventLogWatcher? second;
        lock (_sync)
        {
            _stopRequested = true;
            _running = false;
            first = _watcher261;
            second = _watcher1149;
            _watcher261 = null;
            _watcher1149 = null;
            _retryTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _tcpTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        DisposeWatcher(first);
        DisposeWatcher(second);
    }

    private void TryStartLocked()
    {
        if (_disposed || _running || _stopRequested) return;
        _startAttempts++;

        try
        {
            _watcher261 = CreateWatcher(Query261, OnRdp261);
            _watcher1149 = CreateWatcher(Query1149, OnRdp1149);
            _running = true;
            _lastError = null;
            _startAttempts = 0;
            _retryTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _tcpTimer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(250));
            _logger.Info("Мониторинг RDP-подключений запущен (журнал RemoteConnectionManager, событие 261).");
        }
        catch (Exception ex)
        {
            _lastError = Describe(ex);
            _running = false;
            DisposeWatcher(_watcher261);
            DisposeWatcher(_watcher1149);
            _watcher261 = null;
            _watcher1149 = null;
            _tcpTimer.Change(Timeout.Infinite, Timeout.Infinite);

            if (_startAttempts == 1 || _startAttempts % 10 == 0)
                _logger.Warn("Мониторинг RDP-подключений (261) не запущен: " + _lastError);

            _retryTimer.Change(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
        }
    }

    private EventLogWatcher CreateWatcher(string queryText, EventHandler<EventRecordWrittenEventArgs> handler)
    {
        var watcher = new EventLogWatcher(new EventLogQuery(LogName, PathType.LogName, queryText));
        watcher.EventRecordWritten += handler;
        watcher.Enabled = true;
        return watcher;
    }

    private void RetryTick()
    {
        lock (_sync)
        {
            if (_disposed || _stopRequested || _running) return;
            TryStartLocked();
        }
    }

    private void PollTcp()
    {
        if (!_running || _disposed) return;

        try
        {
            var connections = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections();
            var snapshots = connections.Select(connection => new TcpConnectionSnapshot(
                connection.LocalEndPoint.Address.ToString(),
                connection.LocalEndPoint.Port,
                connection.RemoteEndPoint.Address.ToString(),
                connection.RemoteEndPoint.Port,
                connection.State.ToString()));

            foreach (var ip in _tcpTracker.Observe(snapshots))
                SourceObserved?.Invoke(this, ip);
        }
        catch (Exception ex)
        {
            if (!string.Equals(_lastError, ex.Message, StringComparison.Ordinal))
            {
                _lastError = ex.Message;
                _logger.Warn("Опрос RDP TCP-подключений: " + ex.Message);
            }
        }
    }

    private void OnRdp261(object? sender, EventRecordWrittenEventArgs e)
    {
        using var record = e.EventRecord;
        if (record is null || record.Id != 261) return;
        try { ConnectionObserved?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { _logger.Warn("Обработка 261: " + ex.Message); }
    }

    private void OnRdp1149(object? sender, EventRecordWrittenEventArgs e)
    {
        using var record = e.EventRecord;
        if (record is null || record.Id != 1149) return;

        var address = ExtractAddress(record);
        if (address is null) return;

        try { Authenticated?.Invoke(this, address); }
        catch (Exception ex) { _logger.Warn("Обработка 1149: " + ex.Message); }
    }

    private static string? ExtractAddress(EventRecord record)
    {
        try
        {
            var xml = XDocument.Parse(record.ToXml());
            foreach (var element in xml.Descendants())
            {
                var name = element.Name.LocalName;
                if (!name.Equals("Address", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("IpAddress", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("SourceNetworkAddress", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TryParse(element.Value, out var value)) return value;
            }
        }
        catch { }

        try
        {
            var text = record.FormatDescription() ?? "";
            var match = Regex.Match(
                text,
                @"(?:Адрес источника сети|Source Network Address)\s*:\s*([0-9a-fA-F:.]+)",
                RegexOptions.IgnoreCase);
            if (match.Success && TryParse(match.Groups[1].Value, out var value)) return value;
        }
        catch { }

        return null;
    }

    private static bool TryParse(string? text, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!IPAddress.TryParse(text.Trim(), out var address) || address is null) return false;
        normalized = IpMatcher.Normalize(address).ToString();
        return true;
    }

    private static string Describe(Exception ex)
    {
        var message = ex.Message.Trim();
        return message.Length == 0 ? ex.GetType().Name : message;
    }

    private void DisposeWatcher(EventLogWatcher? watcher)
    {
        if (watcher is null) return;
        try { watcher.Enabled = false; } catch { }
        try { watcher.Dispose(); } catch { }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _running = false;
            _stopRequested = true;
            _retryTimer.Dispose();
            _tcpTimer.Dispose();
        }

        EventLogWatcher? first;
        EventLogWatcher? second;
        lock (_sync)
        {
            first = _watcher261;
            second = _watcher1149;
            _watcher261 = null;
            _watcher1149 = null;
        }

        DisposeWatcher(first);
        DisposeWatcher(second);
    }
}

public sealed class NoopRdp261Monitor : IRdp261Monitor
{
    public string Name => "RDP-подключения (отключено)";
    public int Port { get; set; } = 3389;
    public bool IsRunning { get; private set; }
    public string? LastError => null;

    public event EventHandler? ConnectionObserved
    {
        add { }
        remove { }
    }

    public event EventHandler<string>? SourceObserved
    {
        add { }
        remove { }
    }

    public event EventHandler<string>? Authenticated
    {
        add { }
        remove { }
    }

    public void Start() => IsRunning = true;
    public void Stop() => IsRunning = false;
    public void Dispose() => IsRunning = false;
}
