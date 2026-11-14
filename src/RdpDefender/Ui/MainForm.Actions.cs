using System.Diagnostics;
using System.ServiceProcess;
using RdpDefender.Core;
using RdpDefender.Infrastructure;
using RdpDefender.Ipc;
using ServiceInstaller = RdpDefender.Service.ServiceInstaller;

namespace RdpDefender.Ui;

public sealed partial class MainForm
{
    private async void UnblockSelected(object? sender, EventArgs e)
    {
        var selected = SelectedBlocked();
        if (selected.Count == 0)
        {
            UiDialog.Info(this, "Разблокировка", "Выберите адрес в таблице.");
            return;
        }

        if (!UiDialog.Confirm(this, "Разблокировка",
                $"Снять блокировку с {selected.Count} адрес(ов)? Правила брандмауэра будут удалены."))
            return;

        var failures = 0;
        foreach (var block in selected)
        {
            var ok = await SendCommandAsync(new ControlRequest
            {
                Cmd = "unblock",
                Ip = block.Ip,
                Reason = "Разблокирован вручную"
            }, showError: false);

            if (!ok) failures++;
        }

        await PollAsync();

        if (failures > 0)
            UiDialog.Error(this, "Разблокировка",
                $"Не удалось снять блокировку с {failures} адрес(ов). Проверьте журнал.");
    }

    private async void UnblockAll(object? sender, EventArgs e)
    {
        if (!UiDialog.Confirm(this, "Снять все блокировки",
                "Снять все активные блокировки и удалить правила брандмауэра?"))
            return;

        if (await SendCommandAsync(new ControlRequest
            {
                Cmd = "unblockall",
                Reason = "Все блокировки сняты вручную"
            }))
        {
            await PollAsync();
        }
    }

    private async void BlockIp(object? sender, EventArgs e)
    {
        using var dialog = new BlockIpDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        if (await SendCommandAsync(new ControlRequest
            {
                Cmd = "block",
                Ip = dialog.Ip,
                Minutes = dialog.Minutes,
                Reason = "Заблокирован вручную"
            }))
        {
            await PollAsync();
        }
    }

    private void OpenDataFolder()
    {
        try
        {
            var paths = GuardPaths.CreateDefault();
            paths.EnsureCreated();
            Process.Start(new ProcessStartInfo(paths.DataDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            UiDialog.Error(this, "Папка данных", ex.Message);
        }
    }

    private void OpenLatestLog()
    {
        try
        {
            var paths = GuardPaths.CreateDefault();
            var file = Directory.Exists(paths.LogDirectory)
                ? Directory.GetFiles(paths.LogDirectory, "*.log")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault()
                : null;

            if (file is null)
            {
                OpenDataFolder();
                return;
            }

            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            UiDialog.Error(this, "Журнал", ex.Message);
        }
    }

    private void InitializeTray()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            Font = Theme.Font(9.25f),
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            Padding = new Padding(4)
        };

        var open = new ToolStripMenuItem("Открыть RDP Defender");
        open.Click += (_, _) => ShowMainWindow();

        _trayToggle = new ToolStripMenuItem("Защита включена");
        _trayToggle.Click += async (_, _) => await ToggleProtectionAsync();

        var unblockAll = new ToolStripMenuItem("Снять все блокировки");
        unblockAll.Click += UnblockAll;

        var exit = new ToolStripMenuItem("Выход");
        exit.Click += (_, _) => ExitApplication();

        menu.Items.Add(open);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_trayToggle);
        menu.Items.Add(unblockAll);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);

        _tray = new NotifyIcon
        {
            Icon = AppIcon.Get(32),
            Text = "RDP Defender",
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    private async Task ToggleProtectionAsync()
    {
        if (_status is null)
        {
            ShowMainWindow();
            SelectPage(3);
            return;
        }

        if (await SendCommandAsync(new ControlRequest
            {
                Cmd = "setenabled",
                Enabled = !_status.Enabled
            }))
        {
            await PollAsync();
        }
    }

    private void ExitApplication()
    {
        _exiting = true;
        _pollTimer.Stop();
        Close();
    }

    private async void InstallService(object? sender, EventArgs e)
    {
        if (_busy) return;

        if (!UiDialog.Confirm(this, "Установка службы",
                "Установить RDP Defender как службу Windows?\n\n" +
                "Служба будет запускаться автоматически при старте системы и работать без входа пользователя."))
            return;

        var installed = await RunServiceActionAsync(
            "Установка службы…",
            () => ServiceInstaller.Install(GuardPaths.ExecutablePath, start: true));

        if (installed)
        {
            UiDialog.Info(this, "Служба установлена",
                "Служба RDP Defender установлена и запущена.\nЗащита работает в фоновом режиме.");
        }

        await PollAsync();
    }

    private async void StartService(object? sender, EventArgs e)
    {
        if (_busy) return;
        if (await RunServiceActionAsync("Запуск службы…", ServiceInstaller.Start))
            await PollAsync();
    }

    private async void StopService(object? sender, EventArgs e)
    {
        if (_busy) return;

        if (!UiDialog.Confirm(this, "Остановка службы",
                "Остановить службу RDP Defender?\n\nВсе активные блокировки будут сняты, защита приостановится."))
            return;

        if (await RunServiceActionAsync("Остановка службы…", ServiceInstaller.Stop))
        {
            ClearStatusUi();
            SetConnected(false, "Служба остановлена");
        }
    }

    private async void RestartService(object? sender, EventArgs e)
    {
        if (_busy) return;
        if (await RunServiceActionAsync("Перезапуск службы…", ServiceInstaller.Restart))
        {
            ClearStatusUi();
            await PollAsync();
        }
    }

    private async void RemoveService(object? sender, EventArgs e)
    {
        if (_busy) return;

        var choice = UiDialog.Choose(
            this,
            "Удаление службы",
            "Удалить службу RDP Defender?\n\nВсе созданные ей правила брандмауэра будут сняты.",
            new[] { "Отмена", "Сохранить данные", "Удалить всё" },
            primaryIndex: 1,
            dangerIndex: 2);

        if (choice < 1) return;

        var purge = choice == 2;
        if (await RunServiceActionAsync(
                "Удаление службы…",
                () => ServiceInstaller.Uninstall(removeData: purge)))
        {
            ClearStatusUi();
            SetConnected(false, "Служба удалена");
        }
    }

    private async Task<bool> RunServiceActionAsync(string message, Action action)
    {
        if (_busy) return false;

        _busy = true;
        _footerInfo.Text = message;
        UpdateServiceUi(_serviceStatus);

        try
        {
            await Task.Run(action);
            return true;
        }
        catch (Exception ex)
        {
            UiDialog.Error(this, "Операция со службой", ex.Message);
            return false;
        }
        finally
        {
            _busy = false;
            UpdateServiceUi(ServiceInstaller.GetStatus());
            if (!_connected)
                SetConnected(false);
        }
    }

    private void ClearStatusUi()
    {
        _status = null;
        _settingsLoaded = false;
        _lastBlockCount = -1;
        _blocksSignature = "";
        _attemptsSignature = "";
        _journalSignature = "";

        _blocksGrid.Rows.Clear();
        _attemptsGrid.Rows.Clear();
        _journalGrid.Rows.Clear();
        _blocksGrid.Invalidate();
        _attemptsGrid.Invalidate();
        _journalGrid.Invalidate();

        _cardActive.Value = "0";
        _cardAttempts.Value = "0";
        _cardTotal.Value = "0";
        _cardMode.Value = "—";
        _cardMode.Accent = Theme.TextDim;

        _svcMonitor.Text = "—";
        _svcFirewall.Text = "—";
        _svcData.Text = "—";
        _svcVersion.Text = $"RDP Defender {GuardPaths.Version}";
        _svcError.Text = "";
        _svcError.Visible = false;
        _btnUnblock.Enabled = false;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_preview && !_exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();

            if (!_trayHintShown && _tray is not null)
            {
                _tray.ShowBalloonTip(
                    3000,
                    "RDP Defender",
                    "Программа продолжает работать в трее. Служба защищает сервер.",
                    ToolTipIcon.Info);
                _trayHintShown = true;
            }
            return;
        }

        _pollTimer.Stop();

        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }

        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _cellBold.Dispose();
        base.OnFormClosed(e);
    }
}
