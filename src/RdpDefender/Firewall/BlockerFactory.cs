using RdpDefender.Configuration;
using RdpDefender.Infrastructure;

namespace RdpDefender.Firewall;

/// <summary>Заглушка, если выбранный способ блокировки недоступен.</summary>
public sealed class UnavailableBlocker : IFirewall
{
    public string Name { get; }
    public bool IsAvailable => false;

    public UnavailableBlocker(string method)
    {
        Name = method + " (недоступен)";
    }

    public bool AddBlock(string ip, string ruleName, string description, bool allTraffic, int port) => false;
    public bool RemoveBlock(string ip) => true;
    public bool RuleExists(string ip) => false;
    public bool ClearGroup() => true;
    public bool Shutdown() => true;

    public void Dispose()
    {
    }
}

public static class BlockerFactory
{
    public static IFirewall Create(GuardConfig config, Logger logger)
    {
        if (string.Equals(config.BlockingMethod, BlockerMethods.Firewall, StringComparison.OrdinalIgnoreCase))
            return FirewallFactory.Create(config.FirewallGroup, logger);

        if (string.Equals(config.BlockingMethod, BlockerMethods.IpPolicy, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return new IpPolicyBlocker(logger);
            }
            catch (Exception ex)
            {
                logger.Error("Способ «Политики IP» недоступен: " + ex.Message);
                return new UnavailableBlocker("Политики IP");
            }
        }

        try
        {
            return new WinDivertBlocker(logger);
        }
        catch (Exception ex)
        {
            logger.Error("Способ WinDivert недоступен: " + ex.Message);
            return new UnavailableBlocker("WinDivert");
        }
    }
}
