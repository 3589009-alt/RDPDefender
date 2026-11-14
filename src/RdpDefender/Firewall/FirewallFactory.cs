using RdpDefender.Infrastructure;

namespace RdpDefender.Firewall;

public static class FirewallFactory
{
    public static IFirewall Create(string group, Logger logger)
    {
        try
        {
            var firewall = new ComFirewall(group, logger);
            logger.Info("Брандмауэр: используется COM-интерфейс Windows Firewall.");
            return firewall;
        }
        catch (Exception ex)
        {
            logger.Warn($"COM-интерфейс брандмауэра недоступен ({ex.Message}). Используется netsh.");
            return new NetshFirewall(group, logger);
        }
    }
}
