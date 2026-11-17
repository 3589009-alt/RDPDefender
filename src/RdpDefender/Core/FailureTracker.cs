namespace RdpDefender.Core;

/// <summary>Скользящее окно неудачных попыток входа по каждому IP.</summary>
public sealed class FailureTracker
{
    private sealed class Entry
    {
        public Queue<DateTime> Times { get; } = new();
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public int TrackedIps => _entries.Count;

    public int Register(string ip, DateTime nowUtc, TimeSpan window, int maxTracked)
    {
        if (!_entries.TryGetValue(ip, out var entry))
        {
            entry = new Entry();
            _entries[ip] = entry;
        }

        PruneQueue(entry, nowUtc, window);
        entry.Times.Enqueue(nowUtc);

        if (_entries.Count > Math.Max(100, maxTracked))
            PruneOldest(maxTracked);

        return entry.Times.Count;
    }

    public int Count(string ip, DateTime nowUtc, TimeSpan window)
    {
        if (!_entries.TryGetValue(ip, out var entry)) return 0;
        PruneQueue(entry, nowUtc, window);
        return entry.Times.Count;
    }

    public void Reset(string ip) => _entries.Remove(ip);

    public void Clear() => _entries.Clear();

    public void Prune(DateTime nowUtc, TimeSpan window)
    {
        foreach (var entry in _entries.Values)
            PruneQueue(entry, nowUtc, window);

        foreach (var key in _entries.Where(pair => pair.Value.Times.Count == 0).Select(pair => pair.Key).ToList())
            _entries.Remove(key);
    }

    private static void PruneQueue(Entry entry, DateTime nowUtc, TimeSpan window)
    {
        var threshold = nowUtc - window;
        while (entry.Times.Count > 0 && entry.Times.Peek() < threshold)
            entry.Times.Dequeue();
    }

    private void PruneOldest(int maxTracked)
    {
        var excess = _entries.Count - Math.Max(100, maxTracked);
        if (excess <= 0) return;

        var oldest = _entries
            .OrderBy(pair => pair.Value.Times.Count == 0 ? DateTime.MinValue : pair.Value.Times.Max())
            .Take(excess)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var key in oldest)
            _entries.Remove(key);
    }
}
