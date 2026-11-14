using System.Runtime.InteropServices;
using RdpDefender.Infrastructure;

namespace RdpDefender.Firewall;

/// <summary>
/// Блокировка через COM-интерфейс Windows Firewall (HNetCfg.FwPolicy2).
/// Создаёт по одному входящему правилу на IP в отдельной группе.
/// </summary>
public sealed class ComFirewall : IFirewall
{
    private const int NetFwProfileAll = 0x7FFFFFFF;
    private const int NetFwActionBlock = 0;
    private const int NetFwDirectionIn = 1;
    private const int NetFwProtocolAny = 256;
    private const int ErrorFileNotFound = unchecked((int)0x80070002);

    private readonly Type _policyType;
    private readonly Type _ruleType;
    private readonly string _group;
    private readonly Logger _logger;
    private readonly object _sync = new();

    public string Name => "Windows Firewall (COM)";
    public bool IsAvailable { get; }

    public ComFirewall(string group, Logger logger)
    {
        _group = group;
        _logger = logger;

        _policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2")
            ?? throw new InvalidOperationException("COM-класс HNetCfg.FwPolicy2 не зарегистрирован.");
        _ruleType = Type.GetTypeFromProgID("HNetCfg.FwRule")
            ?? throw new InvalidOperationException("COM-класс HNetCfg.FwRule не зарегистрирован.");

        // Проверяем, что объект политики создаётся и правила доступны.
        var policy = Activator.CreateInstance(_policyType)
            ?? throw new InvalidOperationException("Не удалось создать объект политики брандмауэра.");
        try
        {
            _ = ((dynamic)policy).Rules;
            ProbeRuleCreation();
            IsAvailable = true;
        }
        finally
        {
            Release(policy);
        }
    }

    private void ProbeRuleCreation()
    {
        // Проверяем привязку свойств COM-правила без добавления его в брандмауэр.
        var ruleObject = Activator.CreateInstance(_ruleType)
            ?? throw new InvalidOperationException("Не удалось создать объект правила брандмауэра.");
        try
        {
            dynamic probe = ruleObject;
            probe.Name = "RDP Defender COM probe";
            probe.Description = "probe";
            probe.Grouping = _group;
            probe.Direction = NetFwDirectionIn;
            probe.Action = NetFwActionBlock;
            probe.Protocol = NetFwProtocolAny;
            probe.Profiles = NetFwProfileAll;
            probe.Enabled = false;
            probe.RemoteAddresses = "127.0.0.1";
        }
        finally
        {
            Release(ruleObject);
        }
    }

    public bool AddBlock(string ip, string ruleName, string description, bool allTraffic, int port)
    {
        lock (_sync)
        {
            object? policyObject = null;
            try
            {
                policyObject = Activator.CreateInstance(_policyType)
                    ?? throw new InvalidOperationException("Не удалось создать объект политики брандмауэра.");

                dynamic policy = policyObject;
                dynamic rules = policy.Rules;

                try { rules.Remove(ruleName); } catch { /* правила ещё нет */ }

                var ruleObject = Activator.CreateInstance(_ruleType)
                    ?? throw new InvalidOperationException("Не удалось создать объект правила брандмауэра.");
                dynamic rule = ruleObject;
                rule.Name = ruleName;
                rule.Description = description;
                rule.Grouping = _group;
                rule.Direction = NetFwDirectionIn;
                rule.Action = NetFwActionBlock;
                rule.Protocol = allTraffic ? NetFwProtocolAny : 6;
                if (!allTraffic)
                    rule.LocalPorts = port.ToString();
                rule.Profiles = NetFwProfileAll;
                rule.Enabled = true;
                rule.RemoteAddresses = ip;

                rules.Add(rule);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Не удалось создать правило брандмауэра для {ip}: {ex.Message}");
                return false;
            }
            finally
            {
                if (policyObject is not null) Release(policyObject);
            }
        }
    }

    public bool RemoveBlock(string ip)
    {
        var ruleName = FirewallRules.RuleName(ip);
        lock (_sync)
        {
            object? policyObject = null;
            try
            {
                policyObject = Activator.CreateInstance(_policyType);
                if (policyObject is null) return false;

                dynamic policy = policyObject;
                dynamic rules = policy.Rules;
                rules.Remove(ruleName);
                return true;
            }
            catch (COMException ex) when (ex.HResult == ErrorFileNotFound)
            {
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn($"Не удалось удалить правило «{ruleName}»: {ex.Message}");
                return false;
            }
            finally
            {
                if (policyObject is not null) Release(policyObject);
            }
        }
    }

    public bool RuleExists(string ip)
    {
        var ruleName = FirewallRules.RuleName(ip);
        lock (_sync)
        {
            object? policyObject = null;
            try
            {
                policyObject = Activator.CreateInstance(_policyType);
                if (policyObject is null) return false;

                dynamic policy = policyObject;
                dynamic rules = policy.Rules;
                _ = rules.Item(ruleName);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (policyObject is not null) Release(policyObject);
            }
        }
    }

    public bool ClearGroup()
    {
        // netsh умеет удалять правила по группе; COM-перечисление всех правил
        // заметно медленнее и менее предсказуемо.
        return NetshFirewall.DeleteGroup(_group, _logger);
    }

    public bool Shutdown() => ClearGroup();

    private static void Release(object comObject)
    {
        try
        {
            if (Marshal.IsComObject(comObject))
                Marshal.ReleaseComObject(comObject);
        }
        catch { }
    }

    public void Dispose()
    {
    }
}
