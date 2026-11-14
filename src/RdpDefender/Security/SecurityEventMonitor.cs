using System.Diagnostics.Eventing.Reader;
using RdpDefender.Infrastructure;

namespace RdpDefender.Security;

/// <summary>
/// Подписка на журнал Security (событие 4625) с автоматическим перезапуском
/// при временной недоступности журнала и добором событий за последние минуты.
/// </summary>
public sealed class SecurityEventMonitor : ISecurityEventSource
{
    private const string LogName = "Security";
    private const string EventQuery = "*[System[(EventID=4625)]]";

    private readonly Logger _logger;
    private readonly object _sync = new();
    private readonly object _dedupeSync = new();
    private readonly HashSet<long> _seenRecords = new();
    private readonly Queue<long> _seenOrder = new();
    private readonly System.Threading.Timer _retryTimer;

    private EventLogWatcher? _watcher;
    private bool _running;
    private bool _stopRequested;
    private bool _disposed;
    private string? _lastError;
    private int _startAttempts;

    public event EventHandler<FailedLogonEvent>? FailedLogon;

    public string Name => "Журнал Security · событие 4625";

    /// <summary>Насколько глубоко добирать события при старте (в пределах окна наблюдения).</summary>
    public TimeSpan BackfillWindow { get; set; } = TimeSpan.FromMinutes(15);

    public SecurityEventMonitor(Logger logger)
    {
        _logger = logger;
        _retryTimer = new System.Threading.Timer(_ => RetryTick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public string? LastError
    {
        get { lock (_sync) return _lastError; }
    }

    public bool IsRunning
    {
        get { lock (_sync) return _running; }
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

        if (watcher is not null)
        {
            try { watcher.Enabled = false; } catch { }
            try { watcher.Dispose(); } catch { }
            _logger.Info("Мониторинг журнала Security остановлен.");
        }
    }

    private void TryStartLocked()
    {
        if (_disposed || _running || _stopRequested) return;

        _startAttempts++;
        try
        {
            var query = new EventLogQuery(LogName, PathType.LogName, EventQuery);
            var watcher = new EventLogWatcher(query);
            watcher.EventRecordWritten += OnEventRecordWritten;
            watcher.Enabled = true;

            _watcher = watcher;
            _running = true;
            _lastError = null;
            _startAttempts = 0;
            _retryTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _logger.Info("Мониторинг журнала Security запущен (неудачные входы, событие 4625).");

            try
            {
                Backfill();
            }
            catch (Exception ex)
            {
                _logger.Warn($"Не удалось добрать события за прошедший период: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            _lastError = Describe(ex);
            _running = false;

            if (_startAttempts == 1 || _startAttempts % 10 == 0)
                _logger.Error($"Мониторинг журнала Security не запущен: {_lastError}");

            _retryTimer.Change(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
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

    private void Backfill()
    {
        if (BackfillWindow <= TimeSpan.Zero) return;

        var since = DateTime.UtcNow - BackfillWindow;
        var queryText =
            "*[System[(EventID=4625) and " +
            $"TimeCreated[@SystemTime >= '{since:yyyy-MM-ddTHH:mm:ss.fffZ}']]]";

        using var reader = new EventLogReader(new EventLogQuery(LogName, PathType.LogName, queryText));
        var records = new List<EventRecord>();
        EventRecord? record;
        while (records.Count < 2000 && (record = reader.ReadEvent()) is not null)
            records.Add(record);

        // Журнал может отдавать события в обратном порядке; для корректного
        // скользящего окна обрабатываем их от старых к новым.
        records.Sort((left, right) =>
            Nullable.Compare(left.TimeCreated, right.TimeCreated));

        foreach (var item in records)
        {
            using (item)
            {
                try { Process(item); }
                catch (Exception ex) { _logger.Warn($"Пропущено событие 4625 при загрузке: {ex.Message}"); }
            }
        }

        if (records.Count > 0)
            _logger.Info($"Загружено событий 4625 за последние {BackfillWindow.TotalMinutes:0} мин: {records.Count}.");
    }

    private void OnEventRecordWritten(object? sender, EventRecordWrittenEventArgs e)
    {
        if (e.EventException is not null)
        {
            _logger.Warn($"Ошибка чтения события 4625: {e.EventException.Message}");
            return;
        }

        var record = e.EventRecord;
        if (record is null) return;

        try
        {
            Process(record);
        }
        catch (Exception ex)
        {
            _logger.Error($"Не удалось обработать событие 4625: {ex.Message}");
        }
        finally
        {
            record.Dispose();
        }
    }

    private void Process(EventRecord record)
    {
        var recordId = record.RecordId ?? 0;
        if (recordId > 0)
        {
            lock (_dedupeSync)
            {
                if (!_seenRecords.Add(recordId)) return;
                _seenOrder.Enqueue(recordId);
                while (_seenOrder.Count > 10_000)
                    _seenRecords.Remove(_seenOrder.Dequeue());
            }
        }

        var parsed = EventXmlParser.Parse(
            record.ToXml(),
            record.TimeCreated?.ToUniversalTime(),
            recordId);

        if (parsed is null) return;

        try
        {
            FailedLogon?.Invoke(this, parsed);
        }
        catch (Exception ex)
        {
            _logger.Error($"Обработчик события 4625 завершился ошибкой: {ex.Message}");
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        UnauthorizedAccessException =>
            "нет доступа к журналу Security. Служба должна работать от LocalSystem, консольный режим — от администратора.",
        EventLogNotFoundException =>
            "журнал Security не найден или отключён.",
        EventLogException eventLogException =>
            $"ошибка журнала событий: {eventLogException.Message}",
        _ => ex.Message
    };

    public void Dispose()
    {
        Stop();
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _retryTimer.Dispose();
    }
}
