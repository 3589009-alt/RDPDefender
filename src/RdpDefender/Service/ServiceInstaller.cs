using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;
using Microsoft.Win32;
using RdpDefender.Configuration;
using RdpDefender.Firewall;
using RdpDefender.Infrastructure;

namespace RdpDefender.Service;

/// <summary>Установка, удаление и управление службой Windows через sc.exe.</summary>
public static class ServiceInstaller
{
    public const string ServiceName = GuardPaths.ServiceName;
    public const string DisplayName = "RDP Defender";
    public const string Description =
        "Защита удалённого доступа: отслеживает неудачные входы в журнале Security " +
        "и блокирует IP-адреса атакующих через брандмауэр Windows.";

    public static bool IsAdministrator
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    public static ServiceControllerStatus? GetStatus()
    {
        try
        {
            using var controller = new ServiceController(ServiceName);
            return controller.Status;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsInstalled => GetStatus() is not null;

    public static string? GetImagePath()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{ServiceName}");
            return key?.GetValue("ImagePath") as string;
        }
        catch
        {
            return null;
        }
    }

    public static void Install(string? executablePath = null, bool start = true)
    {
        EnsureAdministrator();
        if (IsInstalled)
            throw new InvalidOperationException("Служба RDP Defender уже установлена.");

        var exePath = executablePath ?? GuardPaths.ExecutablePath;
        if (!File.Exists(exePath))
            throw new FileNotFoundException("Исполняемый файл не найден.", exePath);

        var paths = GuardPaths.CreateDefault();
        paths.EnsureCreated();
        if (!File.Exists(paths.ConfigFile))
            new ConfigStore(paths.ConfigFile).Save(new GuardConfig());

        try
        {
            var binPath = $"\\\"{exePath}\\\" --service";
            RunSc($"create {ServiceName} binPath= \"{binPath}\" start= auto DisplayName= \"{DisplayName}\"");
            RunSc($"description {ServiceName} \"{Description}\"");
            RunSc($"config {ServiceName} start= delayed-auto");
            RunSc($"failure {ServiceName} reset= 86400 actions= restart/5000/restart/15000/restart/60000");

            if (start)
                Start();
        }
        catch
        {
            TryDeleteService();
            throw;
        }
    }

    public static void Uninstall(bool removeData = false)
    {
        EnsureAdministrator();

        if (IsInstalled)
        {
            try { Stop(); } catch { }

            RunSc($"delete {ServiceName}");
            for (var i = 0; i < 20 && IsInstalled; i++)
                Thread.Sleep(250);
        }

        NetshFirewall.DeleteGroup(GuardPaths.FirewallGroup, null);
        IpPolicyBlocker.CleanupTemporaryPolicy();

        if (removeData)
        {
            var paths = GuardPaths.CreateDefault();
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (Directory.Exists(paths.DataDirectory))
                        Directory.Delete(paths.DataDirectory, recursive: true);
                    break;
                }
                catch
                {
                    Thread.Sleep(400);
                }
            }
        }
    }

    public static void Start()
    {
        EnsureAdministrator();
        using var controller = new ServiceController(ServiceName);
        if (controller.Status == ServiceControllerStatus.Running) return;

        controller.Start();
        controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(25));
    }

    public static void Stop()
    {
        EnsureAdministrator();
        using var controller = new ServiceController(ServiceName);
        if (controller.Status == ServiceControllerStatus.Stopped) return;
        if (!controller.CanStop)
            throw new InvalidOperationException("Служба не поддерживает остановку.");

        controller.Stop();
        controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
    }

    public static void Restart()
    {
        Stop();
        Start();
    }

    private static void EnsureAdministrator()
    {
        if (!IsAdministrator)
            throw new UnauthorizedAccessException("Для управления службой требуются права администратора.");
    }

    private static void TryDeleteService()
    {
        try
        {
            if (IsInstalled)
                RunSc($"delete {ServiceName}");
        }
        catch { }
    }

    private static void RunSc(string arguments)
    {
        var scPath = Path.Combine(Environment.SystemDirectory, "sc.exe");
        var psi = new ProcessStartInfo
        {
            FileName = scPath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Не удалось запустить sc.exe.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(20_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new System.TimeoutException("sc.exe не ответил за 20 секунд.");
        }

        if (process.ExitCode != 0)
        {
            var output = (stdout + " " + stderr).Trim();
            throw new InvalidOperationException(
                $"Команда sc {arguments.Split(' ')[0]} завершилась с кодом {process.ExitCode}. {output}");
        }
    }
}
