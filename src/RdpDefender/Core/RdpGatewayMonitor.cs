using System.Diagnostics.Eventing.Reader;
using System.Net;
using System.Xml.Linq;
using RdpDefender.Infrastructure;

namespace RdpDefender.Core;

public sealed record RdpGatewayEvent(
    DateTime TimeUtc,
    string IpAddress,
    string UserName,
    string Resource,
    int EventId,
    bool Failed);

public interface IRdpGatewayMonitor : IDisposable
{
    string Name { get; }
    bool IsRunning { get; }
    string? LastError { get; }
    event EventHandler<RdpGatewayEvent>? EventObserved;
    void Start();
    void Stop();
}

/// <summary>
/// Подписка на журнал Microsoft RD Gateway (RDP через HTTP/HTTPS).
/// 304 — неудачная авторизация, 302 — успешное подключение; в событиях есть IP клиента.
/// </summary>
public sealed class RdpGatewayMonitor : IRdpGatewayMonitor
{
    public const string LogName = "Microsoft-Windows-TerminalServices-Gateway/Operational";
    private const string Query = "*[System[(EventID=302 or EventID=304)]]";

    private readonly Logger _logger;
    private readonly object _sync = new();
    private readonly System.Threading.Timer _retryTimer;
    private EventLogWatcher? _watcher;
    private bool _running;
    private bool _stopRequested;
    private bool _disposed;
    private bool _loggedUnavailable;
    private string? _lastError;

    public string Name => "HTTP-шлюз RDP (RD Gateway)";
    public bool IsRunning { get { lock (_sync) return _running; } }
    public string? LastError { get { lock (_sync) return _lastError; } }

    public event EventHandler<RdpGatewayEvent>? EventObserved;

    public RdpGatewayMonitor(Logger logger)
    {
        _logger = logger;
        _retryTimer = new System.Threading.Timer(_ => RetryTick(), null, Timeout.Infinite, Timeout.Infinite);
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
        EventLogWatcher? watcher;
        lock (_sync)
        {
            _stopRequested = true;
            _running = false;
            watcher = _watcher;
            _watcher = null;
            _retryTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        DisposeWatcher(watcher);
    }

    private void TryStartLocked()
    {
        if (_disposed || _running || _stopRequested) return;

        try
        {
            if (!LogExists())
                throw new InvalidOperationException("журнал RD Gateway не установлен");

            var watcher = new EventLogWatcher(new EventLogQuery(LogName, PathType.LogName, Query));
            watcher.EventRecordWritten += OnEventWritten;
            watcher.Enabled = true;
            _watcher = watcher;
            _running = true;
            _lastError = null;
            _retryTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _logger.Info("Мониторинг HTTP-шлюза RDP запущен (RD Gateway, события 302/304).");
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _running = false;

            if (!_loggedUnavailable)
            {
                _loggedUnavailable = true;
                _logger.Info("HTTP-шлюз RDP не обнаружен (журнал RD Gateway недоступен): " + ex.Message);
            }

            _retryTimer.Change(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }
    }

    private void RetryTick()
    {
        lock (_sync)
        {
            if (_disposed || _stopRequested || _running) return;
            TryStartLocked();
        }
    }

    private static bool LogExists()
    {
        try
        {
            using var session = new EventLogSession();
            session.GetLogInformation(LogName, PathType.LogName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void OnEventWritten(object? sender, EventRecordWrittenEventArgs e)
    {
        using var record = e.EventRecord;
        if (record is null) return;

        try
        {
            var info = Parse(record);
            if (info is null) return;
            EventObserved?.Invoke(this, info);
        }
        catch (Exception ex)
        {
            _logger.Warn("Обработка события RD Gateway: " + ex.Message);
        }
    }

    private static RdpGatewayEvent? Parse(EventRecord record)
    {
        var xml = XDocument.Parse(record.ToXml());
        var eventInfo = xml.Descendants().FirstOrDefault(element =>
            element.Name.LocalName.Equals("EventInfo", StringComparison.OrdinalIgnoreCase));

        var ip = FindValue(eventInfo, "IpAddress")
                 ?? FindValue(xml.Root, "IpAddress")
                 ?? FindValue(eventInfo, "Address");
        var user = FindValue(eventInfo, "Username")
                   ?? FindValue(eventInfo, "UserName")
                   ?? "";
        var resource = FindValue(eventInfo, "Resource")
                       ?? FindValue(eventInfo, "RemoteHost")
                       ?? "";

        if (!IPAddress.TryParse((ip ?? "").Trim(), out var address) || address is null)
            return null;

        return new RdpGatewayEvent(
            record.TimeCreated?.ToUniversalTime() ?? DateTime.UtcNow,
            IpMatcher.Normalize(address).ToString(),
            user.Trim(),
            resource.Trim(),
            record.Id,
            record.Id == 304);
    }

    private static string? FindValue(XElement? element, string name)
    {
        if (element is null) return null;
        return element.DescendantsAndSelf()
            .FirstOrDefault(child => child.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    private static void DisposeWatcher(EventLogWatcher? watcher)
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
        }

        EventLogWatcher? watcher;
        lock (_sync)
        {
            watcher = _watcher;
            _watcher = null;
        }

        DisposeWatcher(watcher);
    }
}

public sealed class NoopRdpGatewayMonitor : IRdpGatewayMonitor
{
    public string Name => "HTTP-шлюз RDP (отключено)";
    public bool IsRunning => false;
    public string? LastError => null;

    public event EventHandler<RdpGatewayEvent>? EventObserved
    {
        add { }
        remove { }
    }

    public void Start() { }
    public void Stop() { }
    public void Dispose() { }
}
