using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using RdpDefender.Core;
using RdpDefender.Infrastructure;

namespace RdpDefender.Firewall;

/// <summary>
/// Блокировка через локальную политику IP-безопасности (IPsec).
/// Политика создаётся при первой блокировке, назначается только на время
/// работы службы и полностью удаляется при остановке.
/// </summary>
public sealed class IpPolicyBlocker : IFirewall
{
    private const string PolicyName = "RDP Defender IP Policy";
    private const string FilterListName = "RDP Defender IP Filter";
    private const string FilterActionName = "RDP Defender IP Block";
    private const string RuleName = "RDP Defender IP Rule";

    private readonly Logger _logger;
    private readonly object _sync = new();
    private readonly Dictionary<string, List<RuleSpec>> _rules = new(StringComparer.OrdinalIgnoreCase);
    private bool _available;
    private bool _disposed;
    private bool _shutdownDone;
    private bool _infrastructure;
    private bool _assigned;

    public string Name => "Политики IP (IPsec)";
    public bool IsAvailable => _available;

    public IpPolicyBlocker(Logger logger)
    {
        _logger = logger;
        EnsurePolicyAgent();

        var show = RunNetsh("ipsec static show policy all");
        if ((show.Code != 0 && !IsNoPolicy(show.Output)) || IsStoreFailure(show.Output))
            throw new InvalidOperationException("Не удалось открыть хранилище IPsec: " + Compact(show.Output));

        // Убираем хвосты от предыдущего аварийного завершения (только свою политику).
        DeleteInfrastructure(quiet: true);

        if (HasAssignedPolicy())
            throw new InvalidOperationException(
                "На компьютере уже назначена другая IPsec-политика. " +
                "Способ «Политики IP» отключён, чтобы не заменить её.");

        _available = true;
        _logger.Info("Блокировка: локальные политики IP (IPsec).");
    }

    public bool AddBlock(string ip, string ruleName, string description, bool allTraffic, int port)
    {
        if (!TryNormalize(ip, out var address)) return false;

        lock (_sync)
        {
            if (_disposed || !_available) return false;

            if (!_infrastructure && HasAssignedPolicy())
            {
                _available = false;
                _logger.Error("IPsec: обнаружена другая назначенная политика — блокировка не применяется.");
                return false;
            }

            if (!EnsureInfrastructure())
                return false;

            DeleteRulesForIpLocked(address);

            var specs = BuildSpecs(address, allTraffic, Math.Clamp(port, 1, 65_535));
            var added = new List<RuleSpec>();

            foreach (var spec in specs)
            {
                if (!AddFilter(spec))
                {
                    foreach (var item in added)
                        DeleteFilter(item);

                    _logger.Error($"IPsec: не удалось добавить фильтр для {address}.");
                    return false;
                }

                added.Add(spec);
            }

            _rules[address] = added;

            if (!_assigned)
            {
                var assign = RunNetsh($"ipsec static set policy name=\"{PolicyName}\" assign=yes");
                if (Failed(assign))
                {
                    DeleteRulesForIpLocked(address);
                    _rules.Remove(address);
                    _logger.Error("IPsec: не удалось назначить политику: " + Compact(assign.Output));
                    return false;
                }

                _assigned = true;
            }

            return true;
        }
    }

    public bool RemoveBlock(string ip)
    {
        if (!TryNormalize(ip, out var address)) return true;

        lock (_sync)
        {
            if (_disposed) return true;
            DeleteRulesForIpLocked(address);
            _rules.Remove(address);

            if (_rules.Count == 0)
                DeleteInfrastructure(quiet: false);

            return true;
        }
    }

    public bool RuleExists(string ip) => TryNormalize(ip, out var address) && _rules.ContainsKey(address);

    public bool ClearGroup()
    {
        lock (_sync)
        {
            _rules.Clear();
            DeleteInfrastructure(quiet: true);
            return true;
        }
    }

    public bool Shutdown()
    {
        lock (_sync)
        {
            if (_shutdownDone) return true;
            _shutdownDone = true;
            _rules.Clear();
            DeleteInfrastructure(quiet: true);
            return true;
        }
    }

    public static bool CleanupTemporaryPolicy()
    {
        RunNetsh($"ipsec static set policy name=\"{PolicyName}\" assign=no");
        RunNetsh($"ipsec static delete policy name=\"{PolicyName}\"");
        RunNetsh($"ipsec static delete filterlist name=\"{FilterListName}\"");
        RunNetsh($"ipsec static delete filteraction name=\"{FilterActionName}\"");
        return true;
    }

    private bool EnsureInfrastructure()
    {
        if (_infrastructure) return true;

        var filterList = RunNetsh(
            $"ipsec static add filterlist name=\"{FilterListName}\" description=\"RDP Defender block list\"");
        if (Failed(filterList))
            return InfrastructureError("список фильтров", filterList);

        var action = RunNetsh(
            $"ipsec static add filteraction name=\"{FilterActionName}\" description=\"RDP Defender block\" action=block");
        if (Failed(action))
            return InfrastructureError("действие фильтра", action);

        var policy = RunNetsh(
            $"ipsec static add policy name=\"{PolicyName}\" description=\"RDP Defender temporary block policy\" " +
            "assign=no");
        if (Failed(policy))
            return InfrastructureError("политику", policy);

        var rule = RunNetsh(
            $"ipsec static add rule name=\"{RuleName}\" policy=\"{PolicyName}\" " +
            $"filterlist=\"{FilterListName}\" filteraction=\"{FilterActionName}\" activate=yes");
        if (Failed(rule))
            return InfrastructureError("правило", rule);

        _infrastructure = true;
        _assigned = false;
        return true;
    }

    private bool InfrastructureError(string what, (int Code, string Output) result)
    {
        _logger.Error($"IPsec: не удалось создать {what}: {Compact(result.Output)}");
        DeleteInfrastructure(quiet: true);
        return false;
    }

    private bool AddFilter(RuleSpec spec)
    {
        var portPart = spec.Port is null ? "" : $" dstport={spec.Port.Value}";
        var command =
            $"ipsec static add filter filterlist=\"{FilterListName}\" " +
            $"srcaddr={spec.Source} dstaddr=me protocol={spec.Protocol}{portPart} " +
            "mirrored=no description=\"RDP Defender block\"";

        var result = RunNetsh(command);
        if (!Failed(result)) return true;

        _logger.Warn($"IPsec: не удалось добавить фильтр для {spec.Source}: {Compact(result.Output)}");
        return false;
    }

    private bool DeleteFilter(RuleSpec spec)
    {
        var portPart = spec.Port is null ? "" : $" dstport={spec.Port.Value}";
        var command =
            $"ipsec static delete filter filterlist=\"{FilterListName}\" " +
            $"srcaddr={spec.Source} dstaddr=me protocol={spec.Protocol}{portPart} mirrored=no";

        var result = RunNetsh(command);
        if (!Failed(result) || IsMissing(result.Output)) return true;

        _logger.Debug($"IPsec: фильтр для {spec.Source} не удалён: {Compact(result.Output)}");
        return false;
    }

    private bool DeleteRulesForIpLocked(string address)
    {
        if (!_rules.TryGetValue(address, out var specs)) return true;

        var ok = true;
        foreach (var spec in specs)
            ok &= DeleteFilter(spec);

        return ok;
    }

    private void DeleteInfrastructure(bool quiet)
    {
        var unassign = RunNetsh($"ipsec static set policy name=\"{PolicyName}\" assign=no");
        if (!quiet && Failed(unassign) && !IsMissing(unassign.Output))
            _logger.Warn("IPsec: не удалось снять назначение политики: " + Compact(unassign.Output));
        _assigned = false;

        RunNetsh($"ipsec static delete policy name=\"{PolicyName}\"");
        RunNetsh($"ipsec static delete filterlist name=\"{FilterListName}\"");
        RunNetsh($"ipsec static delete filteraction name=\"{FilterActionName}\"");
        _infrastructure = false;
    }

    private bool HasAssignedPolicy()
    {
        var show = RunNetsh("ipsec static show policy all");
        if (IsNoPolicy(show.Output)) return false;

        foreach (var raw in show.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (Regex.IsMatch(line, @"\bAssigned\s*:\s*(Yes|True)\b", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(line, @"\bНазначена\s*:\s*(Да|Yes|True)\b", RegexOptions.IgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNoPolicy(string output)
    {
        return output.Contains("No policies", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("No Policy", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("не найдена", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("не найдены", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("отсутствует", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStoreFailure(string output)
    {
        return output.Contains("Unable to open Policy Store", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("Cannot open Policy Store", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("открыть хранилище", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("access denied", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("отказано", StringComparison.OrdinalIgnoreCase);
    }

    private List<RuleSpec> BuildSpecs(string address, bool allTraffic, int port)
    {
        if (allTraffic)
            return new List<RuleSpec> { new(address, "ANY", null) };

        return new List<RuleSpec>
        {
            new(address, "TCP", port),
            new(address, "UDP", port)
        };
    }

    private static void EnsurePolicyAgent()
    {
        try
        {
            using var controller = new ServiceController("PolicyAgent");
            if (controller.Status != ServiceControllerStatus.Running)
            {
                controller.Start();
                controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Служба IPsec Policy Agent недоступна. Проверьте службу PolicyAgent. " + ex.Message, ex);
        }
    }

    private static bool TryNormalize(string ip, out string normalized)
    {
        normalized = "";
        if (!IPAddress.TryParse(ip, out var parsed)) return false;
        normalized = IpMatcher.Normalize(parsed).ToString();
        return true;
    }

    private static (int Code, string Output) RunNetsh(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "netsh.exe"),
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            try
            {
                psi.StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
                psi.StandardErrorEncoding = psi.StandardOutputEncoding;
            }
            catch { }

            using var process = Process.Start(psi);
            if (process is null) return (-1, "не удалось запустить netsh.exe");

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(20_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return (-1, "таймаут netsh.exe");
            }

            return (process.ExitCode, (stdout.GetAwaiter().GetResult() + " " + stderr.GetAwaiter().GetResult()).Trim());
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    private static bool Failed((int Code, string Output) result)
    {
        if (result.Code != 0) return true;
        if (string.IsNullOrWhiteSpace(result.Output)) return false;

        var output = result.Output;
        return output.Contains("ERR IPsec", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("error", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("ошибка", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("requires elevation", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("отказано", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("failed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMissing(string output)
    {
        return output.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("не существует", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("не найден", StringComparison.OrdinalIgnoreCase);
    }

    private static string Compact(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "без описания";
        value = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return value.Length > 300 ? value[..300] + "…" : value;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            Shutdown();
        }
    }

    private sealed record RuleSpec(string Source, string Protocol, int? Port);
}
