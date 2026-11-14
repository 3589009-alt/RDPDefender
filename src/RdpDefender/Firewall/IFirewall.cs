namespace RdpDefender.Firewall;

public interface IFirewall : IDisposable
{
    string Name { get; }
    bool IsAvailable { get; }

    bool AddBlock(string ip, string ruleName, string description, bool allTraffic, int port);
    bool RemoveBlock(string ip);
    bool RuleExists(string ip);

    /// <summary>Удаляет все правила своей группы (в том числе оставшиеся после сбоя).</summary>
    bool ClearGroup();

    /// <summary>Полная остановка: снять правила и удалить временную инфраструктуру метода.</summary>
    bool Shutdown();
}

public static class FirewallRules
{
    public static string RuleName(string ip) => $"RDP Defender Block {ip}";
}
