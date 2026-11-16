using System.Globalization;
using System.ServiceProcess;
using RdpDefender.Configuration;
using RdpDefender.Core;
using RdpDefender.Infrastructure;
using RdpDefender.Ipc;
using ServiceInstaller = RdpDefender.Service.ServiceInstaller;

namespace RdpDefender.Ui;

public sealed partial class MainForm
{
    private async Task PollAsync()
    {
        if (_preview || _polling || _exiting) return;

        _polling = true;
        try
        {
            var serviceStatus = ServiceInstaller.GetStatus();
            if (serviceStatus != ServiceControllerStatus.Running)
            {
                UpdateServiceUi(serviceStatus);
                SetConnected(false, serviceStatus is null
                    ? "Служба RDP Defender не установлена."
                    : "Служба RDP Defender остановлена.");
                return;
            }

            var response = await _client.SendAsync(
                new ControlRequest
                {
                    Cmd = "status",
                    Count = 200,
                    AttemptsCount = _attemptsLimit.Value
                }, timeoutMs: 4000);

            if (!response.Ok)
                throw new InvalidOperationException(response.Error ?? "Служба вернула ошибку.");

            var status = response.GetData<GuardStatus>()
                         ?? throw new InvalidOperationException("Служба вернула пустой ответ.");

            SetConnected(true);
            ApplyStatus(status);

            if (DateTime.UtcNow - _lastServiceCheck > TimeSpan.FromSeconds(5))
            {
                _lastServiceCheck = DateTime.UtcNow;
                UpdateServiceUi(ServiceInstaller.GetStatus());
            }
        }
        catch (Exception ex)
        {
            SetConnected(false, ex.Message);

            if (DateTime.UtcNow - _lastServiceCheck > TimeSpan.FromSeconds(2))
            {
                _lastServiceCheck = DateTime.UtcNow;
                UpdateServiceUi(ServiceInstaller.GetStatus());
            }
        }
        finally
        {
            _polling = false;
        }
    }

    private void SetConnected(bool connected, string? error = null)
    {
        _connected = connected;

        if (connected)
        {
            _footerStatus.Text = "●  Служба на связи";
            _footerStatus.ForeColor = Theme.Green;
            if (!string.IsNullOrEmpty(error)) _footerInfo.Text = "";
            return;
        }

        _footerStatus.Text = "●  Нет связи со службой";
        _footerStatus.ForeColor = Theme.Danger;
        _footerInfo.Text = string.IsNullOrWhiteSpace(error) ? "Служба остановлена или недоступна" : error;
        _statusPill.SetState(UnavailableStatusText(), Theme.TextDim);

        if (_status is null)
        {
            _cardMode.Value = "НЕТ СВЯЗИ";
            _cardMode.Accent = Theme.TextDim;
        }
    }

    private string UnavailableStatusText() => _serviceStatus switch
    {
        null => "СЛУЖБА НЕ УСТАНОВЛЕНА",
        ServiceControllerStatus.Running => "СЛУЖБА НЕ ОТВЕЧАЕТ",
        _ => "СЛУЖБА ОСТАНОВЛЕНА"
    };

    private void ApplyStatus(GuardStatus status)
    {
        _status = status;

        if (!status.Enabled)
            _statusPill.SetState("ЗАЩИТА ОТКЛЮЧЕНА", Theme.TextDim);
        else if (status.DryRun)
            _statusPill.SetState("РЕЖИМ НАБЛЮДЕНИЯ", Theme.Warn);
        else
            _statusPill.SetState("ЗАЩИТА ВКЛЮЧЕНА", Theme.Green);

        _cardActive.Value = status.ActiveBlocks.ToString(CultureInfo.InvariantCulture);
        _cardAttempts.Value = status.TotalAttempts.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"));
        _cardTotal.Value = status.TotalBlocks.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"));
        _cardMode.Value = !status.Enabled ? "ВЫКЛ" : status.DryRun ? "НАБЛЮДЕНИЕ" : "БЛОКИРОВКА";
        _cardMode.Accent = !status.Enabled ? Theme.TextDim : status.DryRun ? Theme.Warn : Theme.Green;

        RefreshBlocks(status.Blocked);
        RefreshSuccessfulLogons(status.SuccessfulLogons, status.Config, status.Blocked);
        RefreshJournal(status.Events);
        RefreshAttempts(status.RecentAttempts, status.TotalRdp261);
        UpdateDiagnostics(status);

        if (!_settingsLoaded)
            LoadSettings(status.Config);
        else
            RefreshWhitelistFromService(status.Config);

        UpdateTray(status);
    }

    private void RefreshBlocks(IReadOnlyList<BlockedIpView> blocks)
    {
        var signature = string.Join("|", blocks.Select(b =>
            $"{b.Ip}:{b.BlockedAtUtc.Ticks}:{b.ExpiresAtUtc?.Ticks}:{b.Attempts}:{b.Simulated}:{b.Applied}:{b.Reason}"));

        if (signature == _blocksSignature)
            return;

        var selectedIp = SelectedBlocked().FirstOrDefault()?.Ip;
        _blocksSignature = signature;

        _blocksGrid.SuspendLayout();
        _blocksGrid.Rows.Clear();

        foreach (var block in blocks)
        {
            var expires = block.ExpiresAtUtc is null
                ? "Бессрочно"
                : block.ExpiresAtUtc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

            var state = block.Simulated ? "Наблюдение" : block.Applied ? "Активна" : "Ожидает";

            var index = _blocksGrid.Rows.Add(
                block.Ip,
                block.BlockedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture),
                expires,
                block.Attempts.ToString(CultureInfo.InvariantCulture),
                state,
                block.Reason);

            _blocksGrid.Rows[index].Tag = block;
        }

        _blocksGrid.ClearSelection();
        if (!string.IsNullOrEmpty(selectedIp))
        {
            foreach (DataGridViewRow row in _blocksGrid.Rows)
            {
                if (row.Tag is BlockedIpView view &&
                    string.Equals(view.Ip, selectedIp, StringComparison.OrdinalIgnoreCase))
                {
                    row.Selected = true;
                    break;
                }
            }
        }
        else
        {
            _blocksGrid.CurrentCell = null;
        }

        _blocksGrid.ResumeLayout();
        _btnUnblock.Enabled = _blocksGrid.SelectedRows.Count > 0;
        ShowSelectedBlockDetails();
    }

    private void BlocksGridOnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_blocksGrid.Rows[e.RowIndex].Tag is not BlockedIpView block) return;
        var style = e.CellStyle;
        if (style is null) return;

        switch (e.ColumnIndex)
        {
            case 0:
                style.ForeColor = Theme.Green;
                style.Font = _cellBold;
                break;
            case 2:
                style.ForeColor = block.Permanent ? Theme.TextMuted : Theme.Text;
                break;
            case 4:
                style.Font = _cellBold;
                style.ForeColor = block.Simulated ? Theme.Warn : block.Applied ? Theme.Green : Theme.Warn;
                break;
            case 5:
                style.ForeColor = Theme.TextMuted;
                break;
        }
    }

    private void RefreshJournal(IReadOnlyList<JournalEntry> events)
    {
        var signature = events.Count == 0
            ? "0"
            : $"{events[^1].Id}:{events.Count}";

        if (signature == _journalSignature)
            return;

        var wasAtBottom = _journalGrid.Rows.Count == 0 ||
                          _journalGrid.FirstDisplayedScrollingRowIndex + _journalGrid.DisplayedRowCount(false) >=
                          _journalGrid.Rows.Count - 1;

        _journalSignature = signature;
        _journalGrid.SuspendLayout();
        _journalGrid.Rows.Clear();

        foreach (var entry in events)
        {
            var displayLevel = entry.Level switch
            {
                "WARN" => "ВНИМАНИЕ",
                "ERROR" => "ОШИБКА",
                "DEBUG" => "ОТЛАДКА",
                _ => "ИНФО"
            };

            var index = _journalGrid.Rows.Add(
                entry.LocalTime.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                displayLevel,
                entry.Message);

            _journalGrid.Rows[index].Tag = entry;
        }

        _journalGrid.ResumeLayout();
        _journalGrid.CurrentCell = null;
        _journalGrid.ClearSelection();

        if (wasAtBottom && _journalGrid.Rows.Count > 0)
        {
            try { _journalGrid.FirstDisplayedScrollingRowIndex = _journalGrid.Rows.Count - 1; }
            catch { }
        }
    }

    private void RefreshSuccessfulLogons(
        IReadOnlyList<SuccessfulLogonView> successes,
        GuardConfig config,
        IReadOnlyList<BlockedIpView> blocks)
    {
        var signature = (config.WhitelistSuccessfulLogins ? "1" : "0") + "|" +
            string.Join("|", successes.Select(s =>
                $"{s.Ip}:{s.FirstSuccessUtc.Ticks}:{s.LastSuccessUtc.Ticks}:{s.Count}:{s.Source}"));

        if (signature != _successSignature)
        {
            _successSignature = signature;
            _successGrid.SuspendLayout();
            _successGrid.Rows.Clear();

            foreach (var success in successes)
            {
                var address = System.Net.IPAddress.TryParse(success.Ip, out var parsed) ? parsed : null;
                var blacklisted = address is not null &&
                                  IpMatcher.IsMatch(address, config.PermanentBlocklist, out _);
                var whitelisted = address is not null &&
                                  IpMatcher.IsMatch(address, config.Whitelist, out _);
                var activeBlock = blocks.Any(b =>
                    string.Equals(b.Ip, success.Ip, StringComparison.OrdinalIgnoreCase));

                var state = blacklisted ? "Чёрный список"
                    : activeBlock ? "Заблокирован"
                    : whitelisted ? "Белый список"
                    : config.WhitelistSuccessfulLogins ? "Защищён"
                    : "Может блокироваться";

                var index = _successGrid.Rows.Add(
                    success.Ip,
                    success.FirstSuccessUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture),
                    success.LastSuccessUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture),
                    success.Count.ToString(CultureInfo.InvariantCulture),
                    string.IsNullOrWhiteSpace(success.Source) ? "—" : success.Source,
                    string.IsNullOrWhiteSpace(success.UserName) ? "—" : success.UserName,
                    string.IsNullOrWhiteSpace(success.Workstation) ? "—" : success.Workstation,
                    state);

                _successGrid.Rows[index].Tag = success;
            }

            _successGrid.ResumeLayout();
            _successGrid.ClearSelection();
            _successGrid.CurrentCell = null;
        }

        _successInfo.Text = $"Защита успешных подключений: " +
            $"{(config.WhitelistSuccessfulLogins ? "включена" : "отключена")} · IP: {successes.Count}";
    }

    private void RefreshAttempts(IReadOnlyList<FailedAttemptView> attempts, int totalRdp261)
    {
        var signature = totalRdp261 + "|" + string.Join("|", attempts.Select(attempt =>
            $"{attempt.TimeUtc.Ticks}:{attempt.Ip}:{attempt.UserName}:{attempt.LogonType}:{attempt.Outcome}"));

        if (signature == _attemptsSignature)
            return;

        _attemptsSignature = signature;
        _attemptsGrid.SuspendLayout();
        _attemptsGrid.Rows.Clear();

        foreach (var attempt in attempts)
        {
            var index = _attemptsGrid.Rows.Add(
                attempt.TimeUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                attempt.Ip,
                string.IsNullOrWhiteSpace(attempt.UserName) ? "—" : attempt.UserName,
                LogonTypeText(attempt.LogonType),
                string.IsNullOrWhiteSpace(attempt.Workstation) ? "—" : attempt.Workstation,
                attempt.Outcome);

            _attemptsGrid.Rows[index].Tag = attempt;
            if (!string.IsNullOrWhiteSpace(attempt.Status) || !string.IsNullOrWhiteSpace(attempt.SubStatus))
            {
                _attemptsGrid.Rows[index].Cells[5].ToolTipText =
                    $"Status: {attempt.Status}; SubStatus: {attempt.SubStatus}";
            }
        }

        _attemptsGrid.ResumeLayout();
        _attemptsGrid.CurrentCell = null;
        _attemptsGrid.ClearSelection();
        _attemptsInfo.Text = attempts.Count == 0
            ? $"RDP-подключений (261): {totalRdp261}"
            : $"RDP-подключений (261): {totalRdp261} · показано: {attempts.Count}";

        CancelAttemptWhois();
        _attemptsDetails?.Clear("Выберите попытку входа в списке — здесь появятся данные IP и WHOIS.");
    }

    private void AttemptsGridOnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_attemptsGrid.Rows[e.RowIndex].Tag is not FailedAttemptView attempt) return;
        var style = e.CellStyle;
        if (style is null) return;

        switch (e.ColumnIndex)
        {
            case 0:
                style.ForeColor = Theme.TextDim;
                break;
            case 1:
                style.ForeColor = Theme.Green;
                style.Font = _cellBold;
                break;
            case 3:
                style.ForeColor = Theme.TextMuted;
                break;
            case 5:
                style.Font = _cellBold;
                style.ForeColor = attempt.Outcome switch
                {
                    "Заблокирован" => Theme.Green,
                    _ when attempt.Outcome.StartsWith("Белый список", StringComparison.Ordinal) => Theme.Warn,
                    "Уже заблокирован" => Theme.TextMuted,
                    "Защита отключена" => Theme.TextMuted,
                    "Тип входа не отслеживается" => Theme.TextMuted,
                    _ when attempt.Outcome.StartsWith("Не заблокирован", StringComparison.Ordinal) => Theme.Danger,
                    _ => Theme.Text
                };
                break;
        }
    }

    private static string LogonTypeText(int logonType) => logonType switch
    {
        0 => "RDP-подключение",
        2 => "2 · интерактивный",
        3 => "3 · сетевой",
        7 => "7 · разблокировка",
        10 => "10 · RDP",
        _ => logonType.ToString(CultureInfo.InvariantCulture)
    };

    private void JournalGridOnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (_journalGrid.Rows[e.RowIndex].Tag is not JournalEntry entry) return;
        var style = e.CellStyle;
        if (style is null) return;

        switch (e.ColumnIndex)
        {
            case 0:
                style.ForeColor = Theme.TextDim;
                break;
            case 1:
                style.ForeColor = entry.Level switch
                {
                    "WARN" => Theme.Warn,
                    "ERROR" => Theme.Danger,
                    "DEBUG" => Theme.TextDim,
                    _ => Theme.GreenDark
                };
                style.Font = _cellBold;
                break;
            case 2:
                style.ForeColor = entry.Level == "ERROR" ? Theme.Danger : Theme.Text;
                break;
        }
    }

    private void UpdateDiagnostics(GuardStatus status)
    {
        _svcMonitor.Text = status.MonitorRunning
            ? $"Активен · {status.MonitorName}"
            : $"Остановлен · {status.MonitorError ?? "нет данных"}";
        _svcMonitor.ForeColor = status.MonitorRunning ? Theme.Green : Theme.Danger;

        _svcFirewall.Text = $"{status.FirewallName} · {(status.FirewallAvailable ? "готов" : "недоступен")}";
        _svcFirewall.ForeColor = status.FirewallAvailable ? Theme.Green : Theme.Warn;

        _svcData.Text = status.DataDirectory;
        _svcVersion.Text = $"RDP Defender {status.Version}";

        var errors = new List<string>();
        if (!string.IsNullOrWhiteSpace(status.MonitorError))
            errors.Add("Мониторинг: " + status.MonitorError);
        if (!string.IsNullOrWhiteSpace(status.FirewallError))
            errors.Add("Блокировка: " + status.FirewallError);

        _svcError.Text = errors.Count == 0 ? "" : string.Join("   ·   ", errors);
        _svcError.Visible = errors.Count > 0;
    }

    private void UpdateServiceUi(ServiceControllerStatus? status)
    {
        _serviceStatus = status;
        if (_svcState is null) return;

        string text;
        Color color;
        switch (status)
        {
            case null:
                text = "Не установлена";
                color = Theme.TextDim;
                break;
            case ServiceControllerStatus.Running:
                text = "Работает";
                color = Theme.Green;
                break;
            case ServiceControllerStatus.StartPending:
                text = "Запускается…";
                color = Theme.Warn;
                break;
            case ServiceControllerStatus.StopPending:
                text = "Останавливается…";
                color = Theme.Warn;
                break;
            case ServiceControllerStatus.Stopped:
                text = "Остановлена";
                color = Theme.Warn;
                break;
            default:
                text = status.Value.ToString();
                color = Theme.TextMuted;
                break;
        }

        _svcState.Text = text;
        _svcState.ForeColor = color;

        var imagePath = status is null ? null : ServiceInstaller.GetImagePath();
        if (status is null)
        {
            _svcPath.Text = "Служба ещё не установлена. Установите её, чтобы защита работала постоянно.";
            _svcPath.ForeColor = Theme.TextMuted;
        }
        else if (!string.IsNullOrEmpty(imagePath) &&
                 !imagePath.Contains(GuardPaths.ExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            _svcPath.Text = imagePath + "   ·   служба указывает на другой файл, переустановите её";
            _svcPath.ForeColor = Theme.Warn;
        }
        else
        {
            _svcPath.Text = imagePath ?? "Путь к исполняемому файлу неизвестен";
            _svcPath.ForeColor = Theme.TextMuted;
        }

        _btnInstall.Enabled = !_busy && status is null;
        _btnStart.Enabled = !_busy && status is not null && status != ServiceControllerStatus.Running;
        _btnStop.Enabled = !_busy && status == ServiceControllerStatus.Running;
        _btnRestart.Enabled = !_busy && status == ServiceControllerStatus.Running;
        _btnRemove.Enabled = !_busy && status is not null;

        if (!_connected)
            _statusPill.SetState(UnavailableStatusText(), status is null ? Theme.TextDim : Theme.Warn);
    }

    private void UpdateTray(GuardStatus status)
    {
        if (_tray is null) return;

        _tray.Text = !status.Enabled
            ? "RDP Defender — защита отключена"
            : status.ActiveBlocks > 0
                ? $"RDP Defender — активных блокировок: {status.ActiveBlocks}"
                : "RDP Defender — защита включена";

        if (_trayToggle is not null)
        {
            _trayToggle.Text = status.Enabled ? "Защита включена" : "Защита отключена";
            _trayToggle.Checked = status.Enabled;
        }

        if (status.Config.NotifyOnBlock && _lastBlockCount >= 0 && status.ActiveBlocks > _lastBlockCount)
        {
            var newest = status.Blocked.FirstOrDefault();
            _tray.ShowBalloonTip(
                4000,
                "RDP Defender",
                newest is not null ? $"Заблокирован IP {newest.Ip}" : "Новая блокировка адреса",
                ToolTipIcon.Warning);
        }

        _lastBlockCount = status.ActiveBlocks;
    }

    private void RefreshWhitelistFromService(GuardConfig config)
    {
        var text = string.Join(Environment.NewLine, config.Whitelist ?? new List<string>());
        if (text == _loadedWhitelist) return;

        // Не затираем несохранённые правки пользователя; обновляем только если
        // текстовое поле всё ещё совпадает с последней загруженной версией.
        if (!string.Equals(_cfgWhitelist.Text, _loadedWhitelist, StringComparison.Ordinal))
            return;

        _cfgWhitelist.Text = text;
        _loadedWhitelist = text;
    }

    private void LoadSettings(GuardConfig config)
    {
        _cfgBlockingMethod.SelectedIndex = BlockerMethods.ToIndex(config.BlockingMethod);
        _cfgEnabled.Checked = config.Enabled;
        _cfgDryRun.Checked = config.DryRun;
        _cfgThreshold.Value = config.FailureThreshold;
        _cfgWindow.Value = config.ObservationWindowMinutes;
        _cfgBlockMinutes.Value = config.BlockDurationMinutes;
        _cfgPermanent.Value = config.PermanentBlockAfter;
        _cfgWhitelist.Text = string.Join(Environment.NewLine, config.Whitelist);
        _loadedWhitelist = _cfgWhitelist.Text;
        _cfgBlacklist.Text = string.Join(Environment.NewLine, config.PermanentBlocklist);
        _cfgBlockAll.Checked = config.BlockAllTraffic;
        _cfgNotify.Checked = config.NotifyOnBlock;
        _cfgLogon3.Checked = config.LogonTypes.Contains(3);
        _cfgLogon10.Checked = config.LogonTypes.Contains(10);
        _cfgRetention.Value = config.JournalRetentionDays;
        _cfgPort.Value = config.RdpPort;
        _cfgRecentAttempts.Value = config.RecentAttemptsCount;
        _cfgRdp261.Checked = config.Rdp261Enabled;
        _cfgRdp261Threshold.Value = config.Rdp261Threshold;
        _cfgRdp261Window.Value = config.Rdp261WindowMinutes;
        _cfgWhitelistSuccess.Checked = config.WhitelistSuccessfulLogins;
        _attemptsLimit.Value = config.RecentAttemptsCount;
        _settingsLoaded = true;
        _saveState.Text = "";
    }

    private GuardConfig BuildConfigFromUi()
    {
        var config = _status?.Config.Clone() ?? LoadConfigFromDisk();

        config.BlockingMethod = BlockerMethods.FromIndex(_cfgBlockingMethod.SelectedIndex);
        config.Enabled = _cfgEnabled.Checked;
        config.DryRun = _cfgDryRun.Checked;
        config.FailureThreshold = _cfgThreshold.Value;
        config.ObservationWindowMinutes = _cfgWindow.Value;
        config.BlockDurationMinutes = _cfgBlockMinutes.Value;
        config.PermanentBlockAfter = _cfgPermanent.Value;
        config.Whitelist = ParseEntries(_cfgWhitelist.Text);
        config.PermanentBlocklist = ParseEntries(_cfgBlacklist.Text);
        config.BlockAllTraffic = _cfgBlockAll.Checked;
        config.NotifyOnBlock = _cfgNotify.Checked;
        config.JournalRetentionDays = _cfgRetention.Value;
        config.RdpPort = _cfgPort.Value;
        config.RecentAttemptsCount = _cfgRecentAttempts.Value;
        config.Rdp261Enabled = _cfgRdp261.Checked;
        config.Rdp261Threshold = _cfgRdp261Threshold.Value;
        config.Rdp261WindowMinutes = _cfgRdp261Window.Value;
        config.WhitelistSuccessfulLogins = _cfgWhitelistSuccess.Checked;

        var types = new List<int>();
        if (_cfgLogon3.Checked) types.Add(3);
        if (_cfgLogon10.Checked) types.Add(10);
        config.LogonTypes = types;

        return config;
    }

    private static List<string> ParseEntries(string text)
    {
        var result = new List<string>();
        foreach (var raw in (text ?? "").Split('\n'))
        {
            var line = raw.Trim();
            var comment = line.IndexOf('#');
            if (comment >= 0) line = line[..comment].Trim();
            if (line.Length == 0) continue;
            if (!result.Contains(line, StringComparer.OrdinalIgnoreCase))
                result.Add(line);
        }
        return result;
    }

    private static GuardConfig LoadConfigFromDisk()
    {
        try
        {
            var paths = GuardPaths.CreateDefault();
            paths.EnsureCreated();
            return new ConfigStore(paths.ConfigFile).Load(out _);
        }
        catch
        {
            return new GuardConfig();
        }
    }

    private async void SaveSettings(object? sender, EventArgs e)
    {
        if (_busy) return;

        var config = BuildConfigFromUi();
        if (await PersistConfigAsync(config))
            SetSaveMessage(_connected
                ? "Настройки сохранены и применены"
                : "Сохранено. Применится при запуске службы.", Theme.Green);
    }

    private async void ResetSettings(object? sender, EventArgs e)
    {
        if (_busy) return;

        if (!UiDialog.Confirm(this, "Сброс настроек",
                "Сбросить все настройки к значениям по умолчанию?\n\n" +
                "Будет выбран WinDivert, белый список заполнится всеми локальными сетями. " +
                "Текущие блокировки не затрагиваются, но правила будут пересозданы."))
            return;

        var defaults = new GuardConfig { ConfigVersion = GuardConfig.CurrentConfigVersion };
        if (await PersistConfigAsync(defaults))
            SetSaveMessage("Настройки сброшены к значениям по умолчанию", Theme.Green);
    }

    private async Task<bool> PersistConfigAsync(GuardConfig config)
    {
        config.Normalize();
        var errors = config.Validate();
        if (errors.Count > 0)
        {
            UiDialog.Error(this, "Проверьте настройки", string.Join(Environment.NewLine, errors));
            return false;
        }

        if (_connected)
        {
            try
            {
                var response = await _client.SendAsync(
                    new ControlRequest { Cmd = "saveconfig", Config = config },
                    timeoutMs: 30_000);
                if (!response.Ok)
                {
                    UiDialog.Error(this, "Не удалось сохранить настройки", response.Error ?? "Неизвестная ошибка.");
                    return false;
                }

                var saved = response.GetData<GuardStatus>();
                if (saved is not null)
                {
                    ApplyStatus(saved);
                    LoadSettings(saved.Config);
                }
                else
                {
                    LoadSettings(config);
                }

                return true;
            }
            catch (Exception ex)
            {
                UiDialog.Error(this, "Не удалось сохранить настройки", ex.Message);
                return false;
            }
        }

        try
        {
            var paths = GuardPaths.CreateDefault();
            paths.EnsureCreated();
            new ConfigStore(paths.ConfigFile).Save(config);
            LoadSettings(config);
            return true;
        }
        catch (Exception ex)
        {
            UiDialog.Error(this, "Не удалось сохранить настройки", ex.Message);
            return false;
        }
    }

    private void MergeLocalNetworks()
    {
        var entries = ParseEntries(_cfgWhitelist.Text);
        var added = 0;

        foreach (var network in GuardConfig.DefaultLocalNetworks)
        {
            if (entries.Contains(network, StringComparer.OrdinalIgnoreCase)) continue;
            entries.Add(network);
            added++;
        }

        _cfgWhitelist.Text = string.Join(Environment.NewLine, entries);
        SetSaveMessage(added == 0
            ? "Все локальные сети уже в списке"
            : $"Добавлено локальных диапазонов: {added}", Theme.Green);
    }

    private void RevertSettings()
    {
        var config = _status?.Config ?? LoadConfigFromDisk();
        LoadSettings(config);
        SetSaveMessage("Изменения отменены", Theme.TextMuted);
    }

    private void SetSaveMessage(string text, Color color)
    {
        _saveState.Text = text;
        _saveState.ForeColor = color;
    }

    private List<BlockedIpView> SelectedBlocked() =>
        _blocksGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => row.Tag)
            .OfType<BlockedIpView>()
            .ToList();

    private async Task<bool> SendCommandAsync(ControlRequest request, bool showError = true)
    {
        try
        {
            var response = await _client.SendAsync(request);
            if (response.Ok) return true;

            if (showError)
                UiDialog.Error(this, "RDP Defender", response.Error ?? "Неизвестная ошибка.");
            return false;
        }
        catch (Exception ex)
        {
            if (showError)
                UiDialog.Error(this, "Нет связи со службой", ex.Message);
            SetConnected(false, ex.Message);
            return false;
        }
    }
}
