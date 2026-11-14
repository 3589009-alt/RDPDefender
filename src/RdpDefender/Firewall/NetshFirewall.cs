using System.Diagnostics;
using System.Globalization;
using System.Text;
using RdpDefender.Infrastructure;

namespace RdpDefender.Firewall;

/// <summary>Резервная реализация через netsh advfirewall (используется, если COM недоступен).</summary>
public sealed class NetshFirewall : IFirewall
{
    private readonly string _group;
    private readonly Logger _logger;
    private readonly object _sync = new();
    private readonly string _netshPath;

    public string Name => "Windows Firewall (netsh)";
    public bool IsAvailable => File.Exists(_netshPath);

    public NetshFirewall(string group, Logger logger)
    {
        _group = group;
        _logger = logger;
        _netshPath = Path.Combine(Environment.SystemDirectory, "netsh.exe");
    }

    public bool AddBlock(string ip, string ruleName, string description, bool allTraffic, int port)
    {
        lock (_sync)
        {
            DeleteRuleNoLock(ruleName);
            var scope = allTraffic ? "" : $" protocol=TCP localport={port}";
            var args =
                "advfirewall firewall add rule " +
                $"name={Quote(ruleName)} dir=in action=block remoteip={ip} " +
                $"profile=any enable=yes group={Quote(_group)} description={Quote(description)}{scope}";

            var result = Run(args);
            if (result.Code == 0) return true;

            _logger.Error($"netsh не создал правило для {ip} (код {result.Code}): {Trim(result.Output)}");
            return false;
        }
    }

    public bool RemoveBlock(string ip)
    {
        var ruleName = FirewallRules.RuleName(ip);
        lock (_sync)
        {
            var result = Run($"advfirewall firewall delete rule name={Quote(ruleName)}");
            if (result.Code == 0) return true;
            _logger.Debug($"netsh: правило «{ruleName}» не удалено (код {result.Code}).");
            return result.Code == 1;
        }
    }

    public bool RuleExists(string ip)
    {
        var ruleName = FirewallRules.RuleName(ip);
        lock (_sync)
        {
            var result = Run($"advfirewall firewall show rule name={Quote(ruleName)}");
            return result.Code == 0;
        }
    }

    public bool ClearGroup()
    {
        return DeleteGroup(_group, _logger);
    }

    public bool Shutdown() => ClearGroup();

    public static bool DeleteGroup(string group, Logger? logger)
    {
        // netsh не умеет удалять правила по группе, поэтому используем
        // штатный командлет. Ошибки подавляем: отсутствие группы — норма.
        var escapedGroup = group.Replace("'", "''");
        var script =
            "$ErrorActionPreference = 'SilentlyContinue'; " +
            $"Get-NetFirewallRule -Group '{escapedGroup}' | Remove-NetFirewallRule; exit 0";

        var powershell = Path.Combine(
            Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell))
            powershell = "powershell.exe";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = powershell,
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process is null) return false;

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                logger?.Warn("Очистка группы правил брандмауэра не завершилась за 30 секунд.");
                return false;
            }

            var output = (stdout.GetAwaiter().GetResult() + " " + stderr.GetAwaiter().GetResult()).Trim();
            if (output.Length > 0 &&
                (output.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
                 output.Contains("отказано", StringComparison.OrdinalIgnoreCase)))
            {
                logger?.Warn($"Не удалось удалить группу правил «{group}»: {Trim(output)}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger?.Warn($"Не удалось очистить группу правил «{group}»: {ex.Message}");
            return false;
        }
    }

    private void DeleteRuleNoLock(string ruleName)
    {
        var result = Run($"advfirewall firewall delete rule name={Quote(ruleName)}");
        if (result.Code is not (0 or 1))
            _logger.Warn($"netsh: не удалось удалить старое правило «{ruleName}» (код {result.Code}).");
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private static string Trim(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        value = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return value.Length > 400 ? value[..400] + "…" : value;
    }

    private static (int Code, string Output) Run(string arguments)
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
                return (-1, "таймаут выполнения netsh.exe");
            }

            var output = (stdout.GetAwaiter().GetResult() + " " + stderr.GetAwaiter().GetResult()).Trim();
            return (process.ExitCode, output);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    public void Dispose()
    {
    }
}
