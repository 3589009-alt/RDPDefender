using System.Text;

namespace RdpDefender.Infrastructure;

/// <summary>
/// Простой потокобезопасный файловый журнал с суточной ротацией
/// и дублированием записей в кольцевой буфер интерфейса.
/// </summary>
public sealed class Logger : IDisposable
{
    private readonly object _sync = new();
    private readonly string _directory;
    private StreamWriter? _writer;
    private DateTime _writerDate = DateTime.MinValue;
    private bool _disposed;

    public LogBuffer Journal { get; }
    public int RetentionDays { get; set; }
    public string? CurrentFile { get; private set; }

    public event Action<JournalEntry>? EntryWritten;

    public Logger(string directory, int retentionDays = 30, LogBuffer? journal = null)
    {
        _directory = directory;
        RetentionDays = Math.Max(1, retentionDays);
        Journal = journal ?? new LogBuffer();
        EnsureDirectory();
        CleanupOldFiles();
    }

    public void Info(string message, string? ip = null) => Write("INFO", message, ip);
    public void Warn(string message, string? ip = null) => Write("WARN", message, ip);
    public void Error(string message, string? ip = null) => Write("ERROR", message, ip);
    public void Debug(string message, string? ip = null) => Write("DEBUG", message, ip);

    private void Write(string level, string message, string? ip)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        JournalEntry? entry = null;
        lock (_sync)
        {
            if (_disposed) return;
            try
            {
                EnsureWriter();
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level,-5}] {message}";
                _writer!.WriteLine(line);
                entry = Journal.Add(level, message, ip);
            }
            catch
            {
                // Журналирование не должно ронять защиту.
            }
        }

        if (entry is not null)
        {
            try { EntryWritten?.Invoke(entry); }
            catch { /* подписчик не должен ломать движок */ }
        }
    }

    private void EnsureDirectory()
    {
        try { Directory.CreateDirectory(_directory); }
        catch { /* каталог может быть недоступен — журнал просто не пишется */ }
    }

    private void EnsureWriter()
    {
        var today = DateTime.Today;
        if (_writer is not null && _writerDate == today) return;

        _writer?.Dispose();
        _writer = null;

        EnsureDirectory();
        var path = Path.Combine(_directory, $"rdpdefender-{today:yyyy-MM-dd}.log");
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        _writerDate = today;
        CurrentFile = path;
    }

    public void CleanupOldFiles()
    {
        try
        {
            if (!Directory.Exists(_directory)) return;
            var threshold = DateTime.Now.AddDays(-RetentionDays);
            foreach (var file in Directory.EnumerateFiles(_directory, "rdpdefender-*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < threshold)
                        File.Delete(file);
                }
                catch { }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            try { _writer?.Dispose(); } catch { }
            _writer = null;
        }
    }
}
