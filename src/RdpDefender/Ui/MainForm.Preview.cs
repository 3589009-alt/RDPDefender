using System.Drawing.Imaging;
using System.ServiceProcess;
using RdpDefender.Configuration;
using RdpDefender.Core;
using RdpDefender.Infrastructure;

namespace RdpDefender.Ui;

public sealed partial class MainForm
{
    private void LoadPreview()
    {
        var now = DateTime.UtcNow;

        var config = new GuardConfig
        {
            Enabled = true,
            DryRun = false,
            FailureThreshold = 5,
            ObservationWindowMinutes = 10,
            BlockDurationMinutes = 1440,
            PermanentBlockAfter = 3,
            Whitelist = new List<string> { "10.10.0.0/16", "192.168.0.10" },
            PermanentBlocklist = new List<string> { "198.51.100.12" },
            WhitelistSuccessfulLogins = true,
            LogonTypes = new List<int> { 3, 10 }
        };
        config.MergeDefaultLocalNetworks();

        var blocks = new List<BlockedIpView>
        {
            new()
            {
                Ip = "203.0.113.45",
                BlockedAtUtc = now.AddMinutes(-42),
                ExpiresAtUtc = now.AddHours(23).AddMinutes(18),
                Attempts = 37,
                Reason = "Превышен порог неудачных входов (37 за 10 мин)",
                BlockNumber = 2,
                Applied = true
            },
            new()
            {
                Ip = "198.51.100.12",
                BlockedAtUtc = now.AddHours(-6),
                ExpiresAtUtc = null,
                Attempts = 0,
                Reason = GuardEngine.BlacklistReason,
                BlockNumber = 1,
                Applied = true,
                Permanent = true
            },
            new()
            {
                Ip = "192.0.2.77",
                BlockedAtUtc = now.AddMinutes(-8),
                ExpiresAtUtc = now.AddMinutes(52),
                Attempts = 8,
                Reason = "Превышен порог неудачных входов (8 за 10 мин)",
                BlockNumber = 1,
                Applied = true
            }
        };

        var events = new List<JournalEntry>
        {
            new()
            {
                Id = 1,
                TimeUtc = now.AddHours(-5),
                Level = "INFO",
                Message = "RDP Defender 1.3.3 запущен. Каталог данных: C:\\ProgramData\\RDP Defender"
            },
            new()
            {
                Id = 2,
                TimeUtc = now.AddMinutes(-44),
                Level = "INFO",
                Message = "Неудачный вход с 203.0.113.45: пользователь «Administrator», тип 3, попытка 5/5.",
                Ip = "203.0.113.45"
            },
            new()
            {
                Id = 3,
                TimeUtc = now.AddMinutes(-42),
                Level = "WARN",
                Message = "IP 203.0.113.45 заблокирован до 14.11.2026 09:15 (попыток: 37, причина: превышен порог).",
                Ip = "203.0.113.45"
            },
            new()
            {
                Id = 4,
                TimeUtc = now.AddMinutes(-12),
                Level = "INFO",
                Message = "Неудачный вход с 192.0.2.77: пользователь «admin», тип 10, попытка 4/5.",
                Ip = "192.0.2.77"
            },
            new()
            {
                Id = 5,
                TimeUtc = now.AddMinutes(-8),
                Level = "WARN",
                Message = "IP 192.0.2.77 заблокирован до 13.11.2026 13:32 (попыток: 8, причина: превышен порог).",
                Ip = "192.0.2.77"
            },
            new()
            {
                Id = 6,
                TimeUtc = now.AddMinutes(-6),
                Level = "INFO",
                Message = "Вход с 192.168.0.10 не учитывается: адрес в белом списке (192.168.0.10).",
                Ip = "192.168.0.10"
            }
        };

        var attempts = new List<FailedAttemptView>
        {
            new()
            {
                TimeUtc = now.AddMinutes(-2), Ip = "78.37.40.185", UserName = "",
                LogonType = 0, Workstation = "", Outcome = "Заблокирован (261, наблюдение)"
            },
            new()
            {
                TimeUtc = now.AddMinutes(-2), Ip = "78.37.40.185", UserName = "",
                LogonType = 0, Workstation = "", Outcome = "RDP-подключение 51 из 50"
            },
            new()
            {
                TimeUtc = now.AddMinutes(-4), Ip = "203.0.113.45", UserName = "Administrator",
                LogonType = 3, Workstation = "WIN-7X2", Status = "0xc000006d", SubStatus = "0xc000006a",
                Outcome = "Заблокирован"
            },
            new()
            {
                TimeUtc = now.AddMinutes(-7), Ip = "203.0.113.45", UserName = "Administrator",
                LogonType = 3, Workstation = "WIN-7X2", Status = "0xc000006d", SubStatus = "0xc000006a",
                Outcome = "Попытка 4 из 5"
            },
            new()
            {
                TimeUtc = now.AddMinutes(-9), Ip = "192.0.2.77", UserName = "admin",
                LogonType = 10, Workstation = "PC-01", Status = "0xc000006d", SubStatus = "0xc000006a",
                Outcome = "Попытка 3 из 5"
            },
            new()
            {
                TimeUtc = now.AddMinutes(-11), Ip = "198.51.100.12", UserName = "root",
                LogonType = 3, Workstation = "SCANNER", Status = "0xc000006d", SubStatus = "0xc000006a",
                Outcome = "Уже заблокирован"
            },
            new()
            {
                TimeUtc = now.AddMinutes(-14), Ip = "192.168.0.10", UserName = "operator",
                LogonType = 10, Workstation = "IT-ADMIN", Status = "0xc000006d", SubStatus = "0xc000006a",
                Outcome = "Белый список: 192.168.0.10"
            },
            new()
            {
                TimeUtc = now.AddMinutes(-18), Ip = "203.0.113.45", UserName = "Administrator",
                LogonType = 3, Workstation = "WIN-7X2", Status = "0xc000006d", SubStatus = "0xc000006a",
                Outcome = "Попытка 2 из 5"
            }
        };

        var successes = new List<SuccessfulLogonView>
        {
            new()
            {
                Ip = "192.168.0.10", FirstSuccessUtc = now.AddDays(-4), LastSuccessUtc = now.AddMinutes(-18),
                Count = 23, UserName = "operator", Workstation = "IT-ADMIN", Source = "RDP 1149"
            },
            new()
            {
                Ip = "10.10.5.7", FirstSuccessUtc = now.AddDays(-1), LastSuccessUtc = now.AddHours(-2),
                Count = 4, UserName = "backup", Workstation = "BR-SRV", Source = "HTTP-шлюз"
            },
            new()
            {
                Ip = "203.0.113.55", FirstSuccessUtc = now.AddHours(-9), LastSuccessUtc = now.AddMinutes(-47),
                Count = 2, UserName = "operator", Workstation = "WS-12", Source = "RDP 1149"
            }
        };

        var status = new GuardStatus
        {
            Enabled = true,
            DryRun = false,
            FirewallName = "WinDivert 2.2.2 (WFP)",
            FirewallAvailable = true,
            MonitorName = "Журнал Security · событие 4625",
            MonitorRunning = true,
            MonitoringSinceUtc = now.AddHours(-38),
            LastEventUtc = now.AddMinutes(-4),
            TotalAttempts = 128,
            TotalRdp261 = 1930,
            TotalBlocks = 17,
            ActiveBlocks = blocks.Count,
            TrackedIps = 6,
            Blocked = blocks,
            RecentAttempts = attempts,
            SuccessfulLogons = successes,
            Events = events,
            Config = config,
            Version = GuardPaths.Version,
            DataDirectory = @"C:\ProgramData\RDP Defender"
        };

        _serviceStatus = ServiceControllerStatus.Running;
        SetConnected(true);
        ApplyStatus(status);
        UpdateServiceUi(ServiceControllerStatus.Running);
        _svcPath.Text = @"C:\Program Files\RDP Defender\RdpDefender.exe --service";
        _svcPath.ForeColor = Theme.TextMuted;
        SelectPage(0);
    }

    public void RenderPreview(string directory)
    {
        Directory.CreateDirectory(directory);
        Show();
        Application.DoEvents();

        var pages = new (string Name, int Index)[]
        {
            ("01-blocks", 0),
            ("02-attempts", 1),
            ("03-success", 2),
            ("04-journal", 3),
            ("05-settings", 4),
            ("06-service", 5)
        };

        foreach (var (name, index) in pages)
        {
            SelectPage(index);

            if (index == 4)
                _settingsScroll.ScrollToTop();

            Application.DoEvents();
            Thread.Sleep(120);
            Application.DoEvents();

            CaptureTo(Path.Combine(directory, name + ".png"));

            if (index == 4)
            {
                _settingsScroll.ScrollTo(470);
                Application.DoEvents();
                Thread.Sleep(120);
                Application.DoEvents();
                CaptureTo(Path.Combine(directory, "05b-settings-bottom.png"));

                _settingsScroll.ScrollTo(940);
                Application.DoEvents();
                Thread.Sleep(120);
                Application.DoEvents();
                CaptureTo(Path.Combine(directory, "05c-settings-advanced.png"));
            }
        }

        Hide();
    }

    private void CaptureTo(string path)
    {
        using var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height);
        DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        bitmap.Save(path, ImageFormat.Png);
    }
}
