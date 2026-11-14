namespace RdpDefender.Security;

public sealed class FailedLogonEvent
{
    public DateTime TimeUtc { get; init; }
    public string Ip { get; init; } = "";
    public string UserName { get; init; } = "";
    public int LogonType { get; init; }
    public string Workstation { get; init; } = "";
    public string Status { get; init; } = "";
    public string SubStatus { get; init; } = "";
    public long RecordId { get; init; }
}

public interface ISecurityEventSource : IDisposable
{
    event EventHandler<FailedLogonEvent>? FailedLogon;

    string Name { get; }
    string? LastError { get; }
    bool IsRunning { get; }

    void Start();
    void Stop();
}
