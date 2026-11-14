using RdpDefender.Configuration;
using RdpDefender.Infrastructure;

namespace RdpDefender.Core;

public sealed class BlockedIpView
{
    public string Ip { get; init; } = "";
    public DateTime BlockedAtUtc { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public int Attempts { get; init; }
    public string Reason { get; init; } = "";
    public int BlockNumber { get; init; }
    public bool Simulated { get; init; }
    public bool Applied { get; init; }
    public bool Permanent { get; init; }
}

public sealed class FailedAttemptView
{
    public DateTime TimeUtc { get; init; }
    public string Ip { get; init; } = "";
    public string UserName { get; init; } = "";
    public int LogonType { get; init; }
    public string Workstation { get; init; } = "";
    public string Status { get; init; } = "";
    public string SubStatus { get; init; } = "";
    public string Outcome { get; set; } = "";

    public FailedAttemptView Clone() => new()
    {
        TimeUtc = TimeUtc,
        Ip = Ip,
        UserName = UserName,
        LogonType = LogonType,
        Workstation = Workstation,
        Status = Status,
        SubStatus = SubStatus,
        Outcome = Outcome
    };
}

public sealed class GuardStatus
{
    public bool Enabled { get; init; }
    public bool DryRun { get; init; }

    public string FirewallName { get; init; } = "";
    public bool FirewallAvailable { get; init; }
    public string? FirewallError { get; init; }

    public string MonitorName { get; init; } = "";
    public bool MonitorRunning { get; init; }
    public string? MonitorError { get; init; }

    public DateTime? MonitoringSinceUtc { get; init; }
    public DateTime? LastEventUtc { get; init; }

    public int TotalAttempts { get; init; }
    public int TotalRdp261 { get; init; }
    public int TotalBlocks { get; init; }
    public int ActiveBlocks { get; init; }
    public int TrackedIps { get; init; }

    public List<BlockedIpView> Blocked { get; init; } = new();
    public List<FailedAttemptView> RecentAttempts { get; init; } = new();
    public List<JournalEntry> Events { get; init; } = new();
    public GuardConfig Config { get; init; } = new();

    public string Version { get; init; } = "";
    public string DataDirectory { get; init; } = "";
    public string ServiceName { get; init; } = GuardPaths.ServiceName;
}
