using RdpDefender.Core;
using RdpDefender.Infrastructure;

namespace RdpDefender.Configuration;

/// <summary>Настройки защиты. Сериализуются в %ProgramData%\RDP Defender\config.json.</summary>
public sealed class GuardConfig
{
    public const int CurrentConfigVersion = 2;

    /// <summary>Версия схемы для миграций. 0 — конфиг старого формата.</summary>
    public int ConfigVersion { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Режим наблюдения: события учитываются, но блокировка не применяется.</summary>
    public bool DryRun { get; set; }

    public int FailureThreshold { get; set; } = 5;
    public int ObservationWindowMinutes { get; set; } = 10;
    public int BlockDurationMinutes { get; set; } = 1440;

    /// <summary>0 — выключено; иначе после N блокировок адрес блокируется навсегда.</summary>
    public int PermanentBlockAfter { get; set; }

    public int MaxActiveBlocks { get; set; } = 2000;
    public int JournalRetentionDays { get; set; } = 30;

    /// <summary>Считать RDP-подключения (событие 261) и блокировать источник при превышении порога.</summary>
    public bool Rdp261Enabled { get; set; } = true;
    public int Rdp261Threshold { get; set; } = 50;
    public int Rdp261WindowMinutes { get; set; } = 10;

    /// <summary>После успешного RDP-входа добавлять IP клиента в белый список.</summary>
    public bool WhitelistSuccessfulLogins { get; set; }

    /// <summary>Сколько последних неудачных попыток показывать в разделе «Попытки» (20–100).</summary>
    public int RecentAttemptsCount { get; set; } = 50;

    public List<int> LogonTypes { get; set; } = new() { 3, 10 };

    /// <summary>
    /// По умолчанию исключены все локальные диапазоны: RFC1918, loopback,
    /// link-local, CGNAT, IPv6 ULA и link-local.
    /// </summary>
    public List<string> Whitelist { get; set; } = DefaultLocalNetworks.ToList();

    public List<string> PermanentBlocklist { get; set; } = new();

    /// <summary>windivert, firewall или ippolicy.</summary>
    public string BlockingMethod { get; set; } = BlockerMethods.WinDivert;

    public bool BlockAllTraffic { get; set; } = true;

    /// <summary>Порт RDP, если BlockAllTraffic = false.</summary>
    public int RdpPort { get; set; } = 3389;

    public bool NotifyOnBlock { get; set; } = true;

    public string FirewallGroup { get; set; } = GuardPaths.FirewallGroup;
    public string PipeName { get; set; } = GuardPaths.PipeName;

    public static IReadOnlyList<string> DefaultLocalNetworks { get; } = new[]
    {
        "10.0.0.0/8",
        "100.64.0.0/10",
        "127.0.0.0/8",
        "169.254.0.0/16",
        "172.16.0.0/12",
        "192.168.0.0/16",
        "::1/128",
        "fc00::/7",
        "fe80::/10"
    };

    /// <summary>Добавляет недостающие локальные диапазоны, не трогая пользовательские записи.</summary>
    public bool MergeDefaultLocalNetworks()
    {
        Whitelist ??= new List<string>();
        var added = false;

        foreach (var entry in DefaultLocalNetworks)
        {
            if (Whitelist.Any(item => string.Equals(item, entry, StringComparison.OrdinalIgnoreCase)))
                continue;

            Whitelist.Add(entry);
            added = true;
        }

        return added;
    }

    public GuardConfig Clone()
    {
        var clone = (GuardConfig)MemberwiseClone();
        clone.LogonTypes = new List<int>(LogonTypes ?? new List<int>());
        clone.Whitelist = new List<string>(Whitelist ?? new List<string>());
        clone.PermanentBlocklist = new List<string>(PermanentBlocklist ?? new List<string>());
        return clone;
    }

    /// <summary>Приводит значения к безопасным диапазонам. Не удаляет некорректные IP — их ловит Validate.</summary>
    public void Normalize()
    {
        FailureThreshold = Math.Clamp(FailureThreshold, 1, 10_000);
        ObservationWindowMinutes = Math.Clamp(ObservationWindowMinutes, 1, 10_080);
        BlockDurationMinutes = Math.Clamp(BlockDurationMinutes, 1, 525_600);
        PermanentBlockAfter = Math.Clamp(PermanentBlockAfter, 0, 10_000);
        MaxActiveBlocks = Math.Clamp(MaxActiveBlocks, 10, 100_000);
        JournalRetentionDays = Math.Clamp(JournalRetentionDays, 1, 3650);
        RecentAttemptsCount = Math.Clamp(RecentAttemptsCount, 20, 100);
        Rdp261Threshold = Math.Clamp(Rdp261Threshold, 1, 10_000);
        Rdp261WindowMinutes = Math.Clamp(Rdp261WindowMinutes, 1, 1440);
        RdpPort = Math.Clamp(RdpPort, 1, 65_535);

        LogonTypes ??= new List<int>();
        LogonTypes = LogonTypes.Where(t => t is >= 1 and <= 10).Distinct().ToList();
        if (LogonTypes.Count == 0)
            LogonTypes = new List<int> { 3, 10 };

        Whitelist = CleanList(Whitelist);
        PermanentBlocklist = CleanList(PermanentBlocklist);

        BlockingMethod = string.IsNullOrWhiteSpace(BlockingMethod)
            ? BlockerMethods.WinDivert
            : BlockingMethod.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(FirewallGroup))
            FirewallGroup = GuardPaths.FirewallGroup;
        FirewallGroup = FirewallGroup.Trim();

        if (string.IsNullOrWhiteSpace(PipeName))
            PipeName = GuardPaths.PipeName;
        PipeName = PipeName.Trim();
    }

    public List<string> Validate()
    {
        var errors = new List<string>();

        if (FailureThreshold < 1 || FailureThreshold > 10_000)
            errors.Add("Порог попыток должен быть от 1 до 10 000.");
        if (ObservationWindowMinutes < 1 || ObservationWindowMinutes > 10_080)
            errors.Add("Окно наблюдения должно быть от 1 до 10 080 минут.");
        if (BlockDurationMinutes < 1 || BlockDurationMinutes > 525_600)
            errors.Add("Длительность блокировки должна быть от 1 до 525 600 минут.");
        if (PermanentBlockAfter < 0 || PermanentBlockAfter > 10_000)
            errors.Add("Порог постоянной блокировки должен быть от 0 до 10 000.");
        if (JournalRetentionDays < 1 || JournalRetentionDays > 3650)
            errors.Add("Срок хранения журнала должен быть от 1 до 3650 дней.");
        if (RecentAttemptsCount < 20 || RecentAttemptsCount > 100)
            errors.Add("Количество последних попыток должно быть от 20 до 100.");
        if (Rdp261Threshold < 1 || Rdp261Threshold > 10_000)
            errors.Add("Порог RDP-подключений должен быть от 1 до 10 000.");
        if (Rdp261WindowMinutes < 1 || Rdp261WindowMinutes > 1440)
            errors.Add("Окно RDP-подключений должно быть от 1 до 1440 минут.");
        if (RdpPort < 1 || RdpPort > 65_535)
            errors.Add("Порт RDP должен быть от 1 до 65 535.");

        if (!BlockerMethods.All.Contains(BlockingMethod))
            errors.Add("Неизвестный способ блокировки. Допустимо: windivert, firewall, ippolicy.");

        ValidateNetworks(Whitelist, "Белый список", errors);
        ValidateNetworks(PermanentBlocklist, "Постоянный чёрный список", errors);

        if (LogonTypes is null || LogonTypes.Count == 0)
            errors.Add("Нужно выбрать хотя бы один тип входа (3 или 10).");

        if (string.IsNullOrWhiteSpace(FirewallGroup))
            errors.Add("Имя группы правил брандмауэра не может быть пустым.");
        if (string.IsNullOrWhiteSpace(PipeName) || PipeName.IndexOfAny(new[] { '\\', '/' }) >= 0)
            errors.Add("Имя канала управления задано некорректно.");

        return errors;
    }

    private static void ValidateNetworks(List<string>? entries, string title, List<string> errors)
    {
        if (entries is null) return;
        foreach (var entry in entries)
        {
            if (IpMatcher.TryParseNetwork(entry, out _, out var error) == false)
                errors.Add($"{title}: «{entry}» — {error}");
        }
    }

    private static List<string> CleanList(List<string>? values)
    {
        if (values is null) return new List<string>();
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in values)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var value = raw.Trim();
            if (seen.Add(value)) result.Add(value);
        }
        return result;
    }
}
