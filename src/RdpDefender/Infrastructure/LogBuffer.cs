using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace RdpDefender.Infrastructure;

public sealed class JournalEntry
{
    public long Id { get; init; }
    public DateTime TimeUtc { get; init; }
    public string Level { get; init; } = "INFO";
    public string Message { get; init; } = "";
    public string? Ip { get; init; }

    [JsonIgnore]
    public DateTime LocalTime => TimeUtc.ToLocalTime();
}

/// <summary>Кольцевой буфер последних записей журнала для отображения в интерфейсе.</summary>
public sealed class LogBuffer
{
    private readonly ConcurrentQueue<JournalEntry> _entries = new();
    private long _nextId;

    public int Capacity { get; }

    public LogBuffer(int capacity = 3000)
    {
        Capacity = Math.Max(100, capacity);
    }

    public JournalEntry Add(string level, string message, string? ip = null)
    {
        var entry = new JournalEntry
        {
            Id = Interlocked.Increment(ref _nextId),
            TimeUtc = DateTime.UtcNow,
            Level = level,
            Message = message,
            Ip = ip
        };

        _entries.Enqueue(entry);
        while (_entries.Count > Capacity && _entries.TryDequeue(out _))
        {
        }

        return entry;
    }

    public IReadOnlyList<JournalEntry> Tail(int count)
    {
        if (count <= 0) return Array.Empty<JournalEntry>();
        var snapshot = _entries.ToArray();
        if (snapshot.Length <= count) return snapshot;
        return snapshot[^count..];
    }

    public long LastId => _nextId;

    public void Clear()
    {
        while (_entries.TryDequeue(out _))
        {
        }
    }
}
