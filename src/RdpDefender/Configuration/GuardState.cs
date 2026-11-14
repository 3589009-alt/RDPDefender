using System.Text.Json.Serialization;
using RdpDefender.Infrastructure;

namespace RdpDefender.Configuration;

public sealed class BlockedIp
{
    public string Ip { get; set; } = "";
    public DateTime BlockedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public int Attempts { get; set; }
    public string Reason { get; set; } = "";
    public int BlockNumber { get; set; }

    /// <summary>Блокировка только в режиме наблюдения — правило брандмауэра не создаётся.</summary>
    public bool Simulated { get; set; }

    /// <summary>Правило брандмауэра действительно создано.</summary>
    public bool Applied { get; set; }

    [JsonIgnore]
    public bool Permanent => ExpiresAtUtc is null;
}

public sealed class GuardState
{
    public List<BlockedIp> Blocks { get; set; } = new();
    public Dictionary<string, int> OffenseCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int TotalBlocks { get; set; }
    public int TotalAttempts { get; set; }
    public int TotalRdp261 { get; set; }
    public DateTime? MonitoringSinceUtc { get; set; }
    public DateTime? LastEventUtc { get; set; }
}

public sealed class StateStore
{
    public string Path { get; }

    public StateStore(string path)
    {
        Path = path;
    }

    public GuardState Load(out string? warning)
    {
        warning = null;
        try
        {
            if (!File.Exists(Path))
                return new GuardState();

            var json = File.ReadAllText(Path);
            var state = System.Text.Json.JsonSerializer.Deserialize<GuardState>(json, JsonDefaults.Options);
            if (state is null)
                return new GuardState();

            state.Blocks ??= new List<BlockedIp>();
            state.OffenseCounts ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            state.Blocks.RemoveAll(b => string.IsNullOrWhiteSpace(b.Ip));
            return state;
        }
        catch (Exception ex)
        {
            warning = $"Не удалось прочитать состояние блокировок ({ex.Message}). Список будет пуст.";
            return new GuardState();
        }
    }

    public void Save(GuardState state)
    {
        var dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = System.Text.Json.JsonSerializer.Serialize(state, JsonDefaults.Options);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, Path, overwrite: true);
    }
}
