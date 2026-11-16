using System.Net;
using System.Net.NetworkInformation;
using RdpDefender.Configuration;
using RdpDefender.Firewall;
using RdpDefender.Infrastructure;
using RdpDefender.Security;

namespace RdpDefender.Core;

/// <summary>
/// Движок защиты: получает события 4625, считает попытки по IP,
/// создаёт и снимает блокировки, хранит состояние.
/// </summary>
public sealed class GuardEngine : IDisposable
{
    public const string BlacklistReason = "Постоянный чёрный список";
    private const string ManualReason = "Заблокирован вручную";
    private const int FirewallRetrySeconds = 30;
    private const int RecentAttemptsCapacity = 500;

    private readonly object _sync = new();
    private readonly ConfigStore _configStore;
    private readonly StateStore _stateStore;
    private readonly Logger _logger;
    private IFirewall _firewall;
    private readonly ISecurityEventSource _source;
    private readonly GuardPaths _paths;
    private readonly FailureTracker _tracker = new();
    private readonly FailureTracker _rdp261Tracker = new();
    private readonly Queue<DateTime> _rdp261Times = new();
    private readonly Dictionary<string, Queue<DateTime>> _rdp261Sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<FailedAttemptView> _recentAttempts = new();
    private readonly Dictionary<string, BlockedIp> _blocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _trustedRdp = new(StringComparer.OrdinalIgnoreCase);
    private readonly IRdp261Monitor _rdp261Monitor;
    private readonly IRdpGatewayMonitor _gatewayMonitor;
    private readonly Dictionary<string, DateTime> _whitelistLog = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Threading.Timer _sweepTimer;
    private GuardConfig _config;

    private GuardState _state = new();
    private bool _started;
    private bool _disposed;
    private bool _stateDirty;
    private string? _firewallError;
    private DateTime _lastFirewallRetryUtc = DateTime.MinValue;
    private DateTime _lastMaxBlocksWarningUtc = DateTime.MinValue;

    public GuardEngine(
        GuardConfig config,
        ConfigStore configStore,
        StateStore stateStore,
        Logger logger,
        IFirewall firewall,
        ISecurityEventSource source,
        GuardPaths paths,
        IRdp261Monitor? rdp261Monitor = null,
        IRdpGatewayMonitor? gatewayMonitor = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _configStore = configStore;
        _stateStore = stateStore;
        _logger = logger;
        _firewall = firewall;
        _source = source;
        _paths = paths;
        _rdp261Monitor = rdp261Monitor ?? new Rdp261Monitor(logger);
        _gatewayMonitor = gatewayMonitor ?? new RdpGatewayMonitor(logger);

        _source.FailedLogon += OnFailedLogon;
        _rdp261Monitor.ConnectionObserved += OnRdp261Observed;
        _rdp261Monitor.SourceObserved += OnRdp261SourceObserved;
        _rdp261Monitor.Authenticated += OnRdp1149Authenticated;
        _gatewayMonitor.EventObserved += OnRdpGatewayEvent;
        _sweepTimer = new System.Threading.Timer(_ => OnSweepTimer(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool IsStarted
    {
        get { lock (_sync) return _started; }
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_started || _disposed) return;

            _config.Normalize();
            var errors = _config.Validate();
            if (errors.Count > 0)
                throw new InvalidOperationException("Некорректные настройки: " + string.Join("; ", errors));

            _state = _stateStore.Load(out var stateWarning);
            if (!string.IsNullOrEmpty(stateWarning))
                _logger.Warn(stateWarning);

            var now = DateTime.UtcNow;
            _state.MonitoringSinceUtc ??= now;

            foreach (var block in _state.Blocks)
            {
                if (!IpMatcher.TryParseAddress(block.Ip, out var address) || address is null)
                    continue;
                if (block.ExpiresAtUtc is DateTime expires && expires <= now)
                    continue;

                block.Ip = address.ToString();
                _blocks[block.Ip] = block;
            }

            ReconcileFirewallLocked();
            SyncBlacklistLocked();

            _logger.RetentionDays = _config.JournalRetentionDays;
            _logger.CleanupOldFiles();
            if (_source is SecurityEventMonitor monitor)
                monitor.BackfillWindow = TimeSpan.FromMinutes(Math.Min(_config.ObservationWindowMinutes, 60));

            // Важно: помечаем движок запущенным до Start(), потому что
            // монитор синхронно добирает события 4625 и сразу вызывает обработчик.
            _started = true;
            _source.Start();
            _rdp261Monitor.Port = _config.RdpPort;
            // 1149 нужен для страницы успешных подключений даже при отключённом учёте 261.
            _rdp261Monitor.Start();
            _gatewayMonitor.Start();
            _sweepTimer.Change(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15));
            SaveStateLocked();

            _logger.Info($"RDP Defender {GuardPaths.Version} запущен. Каталог данных: {_paths.DataDirectory}");
            _logger.Info(
                $"Защита: {(_config.Enabled ? "включена" : "отключена")}; режим: {(_config.DryRun ? "наблюдение" : "блокировка")}; " +
                $"порог: {_config.FailureThreshold} за {_config.ObservationWindowMinutes} мин; " +
                $"блокировка: {FormatDuration(_config.BlockDurationMinutes)}; активно: {_blocks.Count}.");
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (!_started) return;
            _started = false;

            try { _source.Stop(); } catch (Exception ex) { _logger.Warn("Остановка мониторинга: " + ex.Message); }
            _rdp261Monitor.Stop();
            _gatewayMonitor.Stop();
            _sweepTimer.Change(Timeout.Infinite, Timeout.Infinite);

            var cleared = _firewall.Shutdown();
            if (!cleared)
            {
                _logger.Warn("Не удалось полностью снять блокировки, снимаю правила по одному.");
                foreach (var block in _blocks.Values)
                {
                    if (block.Simulated) continue;
                    _firewall.RemoveBlock(block.Ip);
                }
            }

            _logger.Info("RDP Defender остановлен. Правила блокировки сняты, состояние сохранено.");
            SaveStateLocked();
        }
    }

    public void SetEnabled(bool enabled)
    {
        lock (_sync)
        {
            if (_config.Enabled == enabled) return;

            _config.Enabled = enabled;
            _configStore.Save(_config);

            if (!enabled)
            {
                var removed = UnblockAllLocked("Защита отключена");
                _logger.Warn($"Защита отключена. Снято блокировок: {removed}.");
            }
            else
            {
                _logger.Info("Защита включена.");
                SyncBlacklistLocked();
            }

            SaveStateLocked();
        }
    }

    public void ApplyConfig(GuardConfig updated)
    {
        if (updated is null) throw new ArgumentNullException(nameof(updated));

        var candidate = updated.Clone();
        candidate.Normalize();
        candidate.ConfigVersion = GuardConfig.CurrentConfigVersion;
        var errors = candidate.Validate();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(Environment.NewLine, errors));

        lock (_sync)
        {
            var previous = _config.Clone();
            _config = candidate;

            try
            {
                _configStore.Save(_config);
            }
            catch
            {
                _config = previous;
                throw;
            }

            _logger.RetentionDays = _config.JournalRetentionDays;
            _logger.CleanupOldFiles();

            _rdp261Monitor.Stop();
            _rdp261Monitor.Port = _config.RdpPort;
            if (_started)
                _rdp261Monitor.Start();

            var methodChanged = !string.Equals(
                previous.BlockingMethod, _config.BlockingMethod, StringComparison.OrdinalIgnoreCase);

            if (methodChanged)
            {
                var oldBlocker = _firewall;
                _firewall = BlockerFactory.Create(_config, _logger);
                oldBlocker.Shutdown();
                oldBlocker.Dispose();
                _logger.Warn($"Способ блокировки изменён: {_firewall.Name}.");
            }

            if (!_config.Enabled)
            {
                UnblockAllLocked("Защита отключена");
            }
            else
            {
                if (methodChanged)
                {
                    foreach (var block in _blocks.Values.ToList())
                    {
                        block.Applied = false;
                        block.Simulated = _config.DryRun;
                        if (!block.Simulated)
                            ApplyFirewallLocked(block);
                    }
                }
                else if (previous.DryRun != _config.DryRun)
                {
                    UnblockAllLocked("Изменён режим работы");
                }
                else if (!_config.DryRun &&
                         (previous.BlockAllTraffic != _config.BlockAllTraffic || previous.RdpPort != _config.RdpPort))
                {
                    foreach (var block in _blocks.Values.Where(b => !b.Simulated).ToList())
                    {
                        _firewall.RemoveBlock(block.Ip);
                        block.Applied = false;
                        ApplyFirewallLocked(block);
                    }
                }

                foreach (var block in _blocks.Values.ToList())
                {
                    if (IpMatcher.TryParseAddress(block.Ip, out var address) && address is not null &&
                        IpMatcher.IsMatch(address, _config.Whitelist, out _))
                    {
                        RemoveBlockLocked(block, "Адрес добавлен в белый список");
                    }
                }

                SyncBlacklistLocked();
            }

            SaveStateLocked();
            _logger.Info(
                $"Настройки применены. Порог: {_config.FailureThreshold}/{_config.ObservationWindowMinutes} мин; " +
                $"блокировка: {FormatDuration(_config.BlockDurationMinutes)}; режим: {(_config.DryRun ? "наблюдение" : "блокировка")}.");
        }
    }

    public void ReloadConfig()
    {
        var config = _configStore.Load(out var warning);
        if (!string.IsNullOrEmpty(warning))
            _logger.Warn(warning);
        ApplyConfig(config);
    }

    public void Block(string ip, int? minutes, string? reason)
    {
        if (!IpMatcher.TryParseAddress(ip, out var address) || address is null)
            throw new ArgumentException("Некорректный IP-адрес.");

        lock (_sync)
        {
            var normalized = address.ToString();
            if (_blocks.ContainsKey(normalized))
                throw new InvalidOperationException("Этот IP уже заблокирован.");

            var permanent = minutes is null;
            var duration = permanent ? (int?)null : Math.Clamp(minutes!.Value, 1, 525_600);
            CreateBlockLocked(
                address,
                0,
                string.IsNullOrWhiteSpace(reason) ? ManualReason : reason.Trim(),
                duration,
                forcePermanent: permanent);
        }
    }

    public bool Unblock(string ip, string reason = "Разблокирован вручную")
    {
        if (!IpMatcher.TryParseAddress(ip, out var address) || address is null)
            return false;

        lock (_sync)
        {
            var normalized = address.ToString();
            if (!_blocks.TryGetValue(normalized, out var block))
                return false;

            RemoveBlockLocked(block, reason);
            SaveStateLocked();
            return true;
        }
    }

    public int UnblockAll(string reason = "Все блокировки сняты")
    {
        lock (_sync)
        {
            var count = UnblockAllLocked(reason);
            if (count > 0) SaveStateLocked();
            return count;
        }
    }

    public GuardStatus GetStatus(int eventCount = 200, int attemptsCount = 50)
    {
        lock (_sync)
        {
            eventCount = Math.Clamp(eventCount, 0, 1000);
            attemptsCount = Math.Clamp(attemptsCount, 20, 100);

            var blockerAvailable = _firewall.IsAvailable;
            var blockerError = _firewallError;
            if (!blockerAvailable && string.IsNullOrEmpty(blockerError))
                blockerError = $"Способ блокировки «{_firewall.Name}» недоступен.";

            return new GuardStatus
            {
                Enabled = _config.Enabled,
                DryRun = _config.DryRun,
                FirewallName = _firewall.Name,
                FirewallAvailable = blockerAvailable && string.IsNullOrEmpty(_firewallError),
                FirewallError = blockerError,
                MonitorName = _source.Name,
                MonitorRunning = _source.IsRunning,
                MonitorError = _source.LastError,
                MonitoringSinceUtc = _state.MonitoringSinceUtc,
                LastEventUtc = _state.LastEventUtc,
                TotalAttempts = _state.TotalAttempts,
                TotalRdp261 = _state.TotalRdp261,
                TotalBlocks = _state.TotalBlocks,
                ActiveBlocks = _blocks.Count,
                TrackedIps = _tracker.TrackedIps,
                Blocked = _blocks.Values
                    .OrderByDescending(b => b.BlockedAtUtc)
                    .Select(ToView)
                    .ToList(),
                RecentAttempts = _recentAttempts
                    .Reverse()
                    .Take(attemptsCount)
                    .Select(attempt => attempt.Clone())
                    .ToList(),
                SuccessfulLogons = _state.SuccessfulLogons
                    .OrderByDescending(s => s.LastSuccessUtc)
                    .Take(500)
                    .Select(s => new SuccessfulLogonView
                    {
                        Ip = s.Ip,
                        FirstSuccessUtc = s.FirstSuccessUtc,
                        LastSuccessUtc = s.LastSuccessUtc,
                        Count = s.Count,
                        UserName = s.UserName,
                        Workstation = s.Workstation,
                        Source = s.Source
                    })
                    .ToList(),
                Events = _logger.Journal.Tail(eventCount).ToList(),
                Config = _config.Clone(),
                Version = GuardPaths.Version,
                DataDirectory = _paths.DataDirectory
            };
        }
    }

    private void OnFailedLogon(object? sender, FailedLogonEvent e)
    {
        try
        {
            lock (_sync)
            {
                if (!_started) return;
                if (!IpMatcher.TryParseAddress(e.Ip, out var address) || address is null) return;
                if (IpMatcher.IsLoopback(address)) return;

                var ip = address.ToString();
                var attempt = new FailedAttemptView
                {
                    TimeUtc = e.TimeUtc,
                    Ip = ip,
                    UserName = DisplayUser(e),
                    LogonType = e.LogonType,
                    Workstation = string.IsNullOrWhiteSpace(e.Workstation) ? "" : e.Workstation,
                    Status = e.Status,
                    SubStatus = e.SubStatus,
                    Outcome = "Учтена"
                };
                RecordAttemptLocked(attempt);

                _state.TotalAttempts++;
                _state.LastEventUtc = _state.LastEventUtc is null || e.TimeUtc > _state.LastEventUtc
                    ? e.TimeUtc
                    : _state.LastEventUtc;
                _stateDirty = true;

                if (IpMatcher.IsMatch(address, _config.PermanentBlocklist, out var blackMatched))
                {
                    attempt.Outcome = string.IsNullOrWhiteSpace(blackMatched)
                        ? "Чёрный список"
                        : $"Чёрный список: {blackMatched}";
                    if (!_blocks.ContainsKey(ip))
                        CreateBlockLocked(address, 0, BlacklistReason, null, forcePermanent: true);
                    return;
                }

                if (IpMatcher.IsMatch(address, _config.Whitelist, out var matched))
                {
                    attempt.Outcome = string.IsNullOrWhiteSpace(matched)
                        ? "Белый список"
                        : $"Белый список: {matched}";
                    LogWhitelistedLocked(ip, matched ?? "", e);
                    return;
                }

                if (IsProtectedSuccessfulLocked(address))
                {
                    attempt.Outcome = "Успешное подключение: не блокируется";
                    return;
                }

                if (!_config.Enabled)
                {
                    attempt.Outcome = "Защита отключена";
                    return;
                }

                if (!_config.LogonTypes.Contains(e.LogonType))
                {
                    attempt.Outcome = "Тип входа не отслеживается";
                    return;
                }

                if (_blocks.ContainsKey(ip))
                {
                    attempt.Outcome = "Уже заблокирован";
                    return;
                }

                var window = TimeSpan.FromMinutes(_config.ObservationWindowMinutes);
                var attempts = _tracker.Register(ip, e.TimeUtc, window, _config.MaxActiveBlocks * 5);
                attempt.Outcome = $"Попытка {attempts} из {_config.FailureThreshold}";

                if (attempts == 1 || attempts == _config.FailureThreshold || attempts % 50 == 0)
                {
                    _logger.Info(
                        $"Неудачный вход с {ip}: пользователь «{DisplayUser(e)}», тип {e.LogonType}, " +
                        $"попытка {attempts}/{_config.FailureThreshold}.",
                        ip);
                }

                if (attempts >= _config.FailureThreshold)
                {
                    var blocked = CreateBlockLocked(
                        address,
                        attempts,
                        $"Превышен порог неудачных входов ({attempts} за {_config.ObservationWindowMinutes} мин)",
                        null,
                        forcePermanent: false);

                    attempt.Outcome = blocked ? "Заблокирован" : "Не заблокирован (лимит)";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка обработки события 4625: " + ex.Message, e.Ip);
        }
    }

    private void RecordAttemptLocked(FailedAttemptView attempt)
    {
        _recentAttempts.Enqueue(attempt);
        while (_recentAttempts.Count > RecentAttemptsCapacity)
            _recentAttempts.Dequeue();
    }

    private void OnRdp261Observed(object? sender, EventArgs e)
    {
        try
        {
            lock (_sync)
            {
                if (!_started || !_config.Enabled || !_config.Rdp261Enabled)
                    return;

                _state.TotalRdp261++;
                _stateDirty = true;

                var now = DateTime.UtcNow;
                var window = TimeSpan.FromMinutes(_config.Rdp261WindowMinutes);

                _rdp261Times.Enqueue(now);
                while (_rdp261Times.Count > 0 && _rdp261Times.Peek() < now - window)
                    _rdp261Times.Dequeue();

                var total = _rdp261Times.Count;
                var ips = GetActiveRdpRemoteIpsLocked();

                foreach (var ip in ips)
                    RegisterRdp261Locked(ip, now, window, total);

                BlockRdp261IfExceededLocked(now, window);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка обработки события 261: " + ex.Message);
        }
    }

    private void OnRdp261SourceObserved(object? sender, string ip)
    {
        try
        {
            lock (_sync)
            {
                if (!_started || !_config.Enabled || !_config.Rdp261Enabled)
                    return;

                var now = DateTime.UtcNow;
                var window = TimeSpan.FromMinutes(_config.Rdp261WindowMinutes);

                if (!TryRdp261CandidateLocked(ip, now, out _, out var normalized))
                    return;

                RememberRdp261SourceLocked(normalized, now, window);

                var total = _rdp261Times.Count;
                var view = new FailedAttemptView
                {
                    TimeUtc = now,
                    Ip = normalized,
                    UserName = "",
                    LogonType = 0,
                    Workstation = "",
                    Outcome = $"RDP-подключение: всего {total} из {_config.Rdp261Threshold}"
                };
                RecordAttemptLocked(view);

                BlockRdp261IfExceededLocked(now, window);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка учёта источника 261: " + ex.Message);
        }
    }

    private void BlockRdp261IfExceededLocked(DateTime now, TimeSpan window)
    {
        var total = _rdp261Times.Count;
        if (total <= _config.Rdp261Threshold) return;

        var blocked = BlockRdp261SourcesLocked(now, window, total);
        _logger.Warn(
            $"Порог RDP-подключений (261) превышен: {total} за {_config.Rdp261WindowMinutes} мин; " +
            $"заблокировано источников: {blocked}.");
        _rdp261Times.Clear();
    }

    private void RegisterRdp261Locked(string ip, DateTime now, TimeSpan window, int total)
    {
        if (!TryRdp261CandidateLocked(ip, now, out _, out var normalized))
            return;

        RememberRdp261SourceLocked(normalized, now, window);

        var perIpAttempts = _rdp261Tracker.Register(
            normalized, now, window, _config.MaxActiveBlocks * 5);

        var view = new FailedAttemptView
        {
            TimeUtc = now,
            Ip = normalized,
            UserName = "",
            LogonType = 0,
            Workstation = "",
            Outcome = $"RDP-подключение: всего {total} из {_config.Rdp261Threshold}"
        };
        RecordAttemptLocked(view);

        if (perIpAttempts == 1 || total % 10 == 0 || total == _config.Rdp261Threshold + 1)
        {
            _logger.Info(
                $"RDP-подключение (261) с {normalized}: всего {total}/{_config.Rdp261Threshold} " +
                $"за {_config.Rdp261WindowMinutes} мин (с этого IP: {perIpAttempts}).",
                normalized);
        }
    }

    private void RememberRdp261SourceLocked(string normalized, DateTime now, TimeSpan window)
    {
        if (!_rdp261Sources.TryGetValue(normalized, out var times))
        {
            times = new Queue<DateTime>();
            _rdp261Sources[normalized] = times;
        }

        times.Enqueue(now);
        while (times.Count > 0 && times.Peek() < now - window)
            times.Dequeue();
    }

    private int BlockRdp261SourcesLocked(DateTime now, TimeSpan window, int total)
    {
        var count = 0;
        foreach (var pair in _rdp261Sources.ToList())
        {
            var times = pair.Value;
            while (times.Count > 0 && times.Peek() < now - window)
                times.Dequeue();

            if (times.Count == 0)
            {
                _rdp261Sources.Remove(pair.Key);
                continue;
            }

            if (!TryRdp261CandidateLocked(pair.Key, now, out var address, out var normalized))
                continue;

            var attempts = times.Count;
            var blocked = CreateBlockLocked(
                address,
                attempts,
                $"Превышено число RDP-подключений (261): {total} за {_config.Rdp261WindowMinutes} мин",
                null,
                forcePermanent: false);

            if (!blocked) continue;

            count++;
            var view = new FailedAttemptView
            {
                TimeUtc = now,
                Ip = normalized,
                UserName = "",
                LogonType = 0,
                Workstation = "",
                Outcome = _config.DryRun ? "Заблокирован (261, наблюдение)" : "Заблокирован (261)"
            };
            RecordAttemptLocked(view);
        }

        return count;
    }

    private bool TryRdp261CandidateLocked(string ip, DateTime now, out IPAddress address, out string normalized)
    {
        address = IPAddress.None;
        normalized = "";

        if (!IPAddress.TryParse(ip, out var parsed) || parsed is null)
            return false;

        address = IpMatcher.Normalize(parsed);
        normalized = address.ToString();

        if (IpMatcher.IsLoopback(address)) return false;
        if (IpMatcher.IsMatch(address, _config.PermanentBlocklist, out _))
        {
            if (!_blocks.ContainsKey(normalized))
                CreateBlockLocked(address, 0, BlacklistReason, null, forcePermanent: true);
            return false;
        }
        if (IpMatcher.IsMatch(address, _config.Whitelist, out _)) return false;
        if (IsProtectedSuccessfulLocked(address)) return false;
        if (_blocks.ContainsKey(normalized)) return false;

        if (_trustedRdp.TryGetValue(normalized, out var trustedUntil))
        {
            if (trustedUntil > now) return false;
            _trustedRdp.Remove(normalized);
        }

        return true;
    }

    private void OnRdpGatewayEvent(object? sender, RdpGatewayEvent e)
    {
        try
        {
            lock (_sync)
            {
                if (!_started || !_config.Enabled) return;
                if (!IPAddress.TryParse(e.IpAddress, out var parsed) || parsed is null) return;
                if (IpMatcher.IsLoopback(parsed)) return;

                var ip = IpMatcher.Normalize(parsed).ToString();
                if (!e.Failed)
                {
                    HandleSuccessfulLoginLocked(ip, "HTTP-шлюз", e.UserName, e.Resource);
                    return;
                }

                if (IpMatcher.IsMatch(parsed, _config.PermanentBlocklist, out _))
                {
                    if (!_blocks.ContainsKey(ip))
                        CreateBlockLocked(parsed, 0, BlacklistReason, null, forcePermanent: true);
                    return;
                }

                if (IpMatcher.IsMatch(parsed, _config.Whitelist, out _)) return;
                if (IsProtectedSuccessfulLocked(parsed)) return;
                if (_blocks.ContainsKey(ip)) return;
                if (_trustedRdp.TryGetValue(ip, out var trustedUntil) && trustedUntil > DateTime.UtcNow)
                    return;

                var now = e.TimeUtc == default ? DateTime.UtcNow : e.TimeUtc;
                var window = TimeSpan.FromMinutes(_config.ObservationWindowMinutes);
                var attempts = _tracker.Register(ip, now, window, _config.MaxActiveBlocks * 5);

                _state.TotalAttempts++;
                _state.LastEventUtc = _state.LastEventUtc is null || now > _state.LastEventUtc
                    ? now
                    : _state.LastEventUtc;
                _stateDirty = true;

                var view = new FailedAttemptView
                {
                    TimeUtc = now,
                    Ip = ip,
                    UserName = string.IsNullOrWhiteSpace(e.UserName) ? "—" : e.UserName,
                    LogonType = 3,
                    Workstation = string.IsNullOrWhiteSpace(e.Resource) ? "HTTP-шлюз" : e.Resource,
                    Status = $"RD Gateway {e.EventId}",
                    SubStatus = "",
                    Outcome = $"HTTP-шлюз: попытка {attempts} из {_config.FailureThreshold}"
                };
                RecordAttemptLocked(view);

                if (attempts == 1 || attempts == _config.FailureThreshold || attempts % 50 == 0)
                {
                    _logger.Info(
                        $"Неудачный вход через HTTP-шлюз с {ip}: пользователь «{e.UserName}», " +
                        $"попытка {attempts}/{_config.FailureThreshold}.");
                }

                if (attempts >= _config.FailureThreshold)
                {
                    var blocked = CreateBlockLocked(
                        parsed,
                        attempts,
                        $"Превышен порог неудачных входов через HTTP-шлюз ({attempts} за " +
                        $"{_config.ObservationWindowMinutes} мин)",
                        null,
                        forcePermanent: false);

                    view.Outcome = blocked ? "Заблокирован (HTTP-шлюз)" : "Не заблокирован (лимит)";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка обработки события RD Gateway: " + ex.Message);
        }
    }

    private void OnRdp1149Authenticated(object? sender, string ip)
    {
        try
        {
            lock (_sync)
            {
                if (!_started) return;
                if (!IPAddress.TryParse(ip, out var parsed) || parsed is null) return;

                var normalized = IpMatcher.Normalize(parsed).ToString();
                HandleSuccessfulLoginLocked(normalized, "RDP 1149");
            }
        }
        catch (Exception ex)
        {
            _logger.Warn("Ошибка обработки успешной аутентификации 1149: " + ex.Message);
        }
    }

    private void HandleSuccessfulLoginLocked(string normalized, string source, string userName = "", string workstation = "")
    {
        if (!IPAddress.TryParse(normalized, out var address) || address is null)
            return;

        address = IpMatcher.Normalize(address);
        normalized = address.ToString();

        if (IpMatcher.IsMatch(address, _config.PermanentBlocklist, out var blackMatched))
        {
            if (!_blocks.ContainsKey(normalized))
                CreateBlockLocked(address, 0, BlacklistReason, null, forcePermanent: true);
            _logger.Warn(
                $"Успешный вход с {normalized} проигнорирован: IP в чёрном списке " +
                $"({blackMatched}).", normalized);
            return;
        }

        var now = DateTime.UtcNow;
        var successful = _state.SuccessfulLogons.FirstOrDefault(s =>
            string.Equals(s.Ip, normalized, StringComparison.OrdinalIgnoreCase));
        if (successful is null)
        {
            successful = new SuccessfulLogon
            {
                Ip = normalized,
                FirstSuccessUtc = now,
                LastSuccessUtc = now,
                Count = 1,
                UserName = userName ?? "",
                Workstation = workstation ?? "",
                Source = source
            };
            _state.SuccessfulLogons.Add(successful);
        }
        else
        {
            successful.LastSuccessUtc = now;
            successful.Count++;
            successful.Source = source;
            if (!string.IsNullOrWhiteSpace(userName)) successful.UserName = userName;
            if (!string.IsNullOrWhiteSpace(workstation)) successful.Workstation = workstation;
        }

        if (_state.SuccessfulLogons.Count > 1000)
        {
            var oldest = _state.SuccessfulLogons
                .OrderBy(s => s.LastSuccessUtc)
                .First();
            _state.SuccessfulLogons.Remove(oldest);
        }

        _tracker.Reset(normalized);
        _rdp261Tracker.Reset(normalized);
        _rdp261Sources.Remove(normalized);
        _state.OffenseCounts.Remove(normalized);
        _stateDirty = true;

        if (_config.WhitelistSuccessfulLogins)
        {
            _trustedRdp[normalized] = now.AddHours(1);
            if (_blocks.TryGetValue(normalized, out var block))
            {
                RemoveBlockLocked(block, $"Успешное подключение ({source})");
                _logger.Info($"IP {normalized}: блокировка снята после успешного подключения ({source}).");
            }
        }
        else
        {
            _trustedRdp.Remove(normalized);
            _logger.Info($"Успешное подключение с {normalized} ({source}); защита успешных IP отключена.");
        }

        _logger.Info($"Успешное подключение с {normalized} ({source}): добавлено в список успешных.");
    }

    private bool IsProtectedSuccessfulLocked(IPAddress address)
    {
        if (!_config.WhitelistSuccessfulLogins) return false;
        var ip = IpMatcher.Normalize(address).ToString();
        return _state.SuccessfulLogons.Any(s =>
            string.Equals(s.Ip, ip, StringComparison.OrdinalIgnoreCase));
    }

    private IReadOnlyList<string> GetActiveRdpRemoteIpsLocked()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var connections = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections();
            foreach (var connection in connections)
            {
                if (connection.LocalEndPoint.Port != _config.RdpPort) continue;
                if (connection.RemoteEndPoint.Port <= 0) continue;
                if (connection.State != TcpState.Established && connection.State != TcpState.SynReceived)
                    continue;

                if (!IPAddress.TryParse(connection.RemoteEndPoint.Address.ToString(), out var address) ||
                    address is null)
                {
                    continue;
                }

                if (IpMatcher.IsLoopback(address)) continue;
                result.Add(IpMatcher.Normalize(address).ToString());
            }
        }
        catch (Exception ex)
        {
            _logger.Debug("Не удалось перечислить RDP-подключения: " + ex.Message);
        }

        return result.ToList();
    }

    private void LogWhitelistedLocked(string ip, string matched, FailedLogonEvent e)
    {
        var now = DateTime.UtcNow;
        if (_whitelistLog.TryGetValue(ip, out var last) && now - last < TimeSpan.FromMinutes(10))
            return;

        if (_whitelistLog.Count > 2000)
            _whitelistLog.Clear();

        _whitelistLog[ip] = now;
        _logger.Info(
            $"Вход с {ip} не учитывается: адрес в белом списке ({matched}). Пользователь «{DisplayUser(e)}».",
            ip);
    }

    private bool CreateBlockLocked(IPAddress address, int attempts, string reason, int? durationMinutes, bool forcePermanent)
    {
        var ip = address.ToString();
        if (_blocks.ContainsKey(ip)) return false;

        if (_blocks.Count >= _config.MaxActiveBlocks)
        {
            if (DateTime.UtcNow - _lastMaxBlocksWarningUtc > TimeSpan.FromMinutes(1))
            {
                _lastMaxBlocksWarningUtc = DateTime.UtcNow;
                _logger.Warn($"Достигнут предел активных блокировок ({_config.MaxActiveBlocks}), {ip} не заблокирован.");
            }
            return false;
        }

        var now = DateTime.UtcNow;
        _state.OffenseCounts.TryGetValue(ip, out var offense);
        offense++;
        _state.OffenseCounts[ip] = offense;

        var permanent = forcePermanent ||
                        (durationMinutes is null && _config.PermanentBlockAfter > 0 && offense >= _config.PermanentBlockAfter);
        var minutes = durationMinutes ?? _config.BlockDurationMinutes;

        var block = new BlockedIp
        {
            Ip = ip,
            BlockedAtUtc = now,
            ExpiresAtUtc = permanent ? null : now.AddMinutes(Math.Max(1, minutes)),
            Attempts = attempts,
            Reason = reason,
            BlockNumber = offense,
            Simulated = _config.DryRun,
            Applied = false
        };

        _blocks[ip] = block;
        _state.TotalBlocks++;
        _tracker.Reset(ip);

        if (_config.DryRun)
        {
            _logger.Warn(
                $"НАБЛЮДЕНИЕ: IP {ip} был бы заблокирован на {FormatDuration(minutes)} " +
                $"(попыток: {attempts}, причина: {reason}).",
                ip);
        }
        else
        {
            var applied = ApplyFirewallLocked(block);
            var until = permanent ? "бессрочно" : $"до {block.ExpiresAtUtc!.Value.ToLocalTime():dd.MM.yyyy HH:mm}";
            if (applied)
            {
                _logger.Warn(
                    $"IP {ip} заблокирован {until} (попыток: {attempts}, причина: {reason}).",
                    ip);
            }
            else
            {
                _logger.Error(
                    $"IP {ip} внесён в список блокировок, но блокировка не применена. " +
                    "Повтор через 30 секунд.",
                    ip);
            }
        }

        SaveStateLocked();
        return true;
    }

    private bool ApplyFirewallLocked(BlockedIp block)
    {
        if (block.Simulated) return false;

        var ok = _firewall.AddBlock(
            block.Ip,
            FirewallRules.RuleName(block.Ip),
            BuildRuleDescription(block),
            _config.BlockAllTraffic,
            _config.RdpPort);

        block.Applied = ok;
        if (ok)
        {
            _firewallError = null;
            return true;
        }

        _firewallError = $"Не удалось применить блокировку для {block.Ip} ({_firewall.Name}).";
        return false;
    }

    private void RemoveBlockLocked(BlockedIp block, string reason)
    {
        _blocks.Remove(block.Ip);
        _stateDirty = true;

        if (!block.Simulated)
        {
            var removed = _firewall.RemoveBlock(block.Ip);
            if (!removed)
                _logger.Warn($"Не удалось снять блокировку для {block.Ip}.", block.Ip);
        }

        _logger.Info($"IP {block.Ip} разблокирован: {reason.ToLowerInvariant()}.", block.Ip);
    }

    private int UnblockAllLocked(string reason)
    {
        var blocks = _blocks.Values.ToList();
        foreach (var block in blocks)
            RemoveBlockLocked(block, reason);
        return blocks.Count;
    }

    private void SyncBlacklistLocked()
    {
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _config.PermanentBlocklist)
        {
            if (!IpMatcher.TryParseAddress(entry, out var address) || address is null)
            {
                _logger.Warn($"Постоянный чёрный список: некорректный адрес «{entry}».");
                continue;
            }
            wanted.Add(address.ToString());
        }

        foreach (var block in _blocks.Values
                     .Where(b => string.Equals(b.Reason, BlacklistReason, StringComparison.Ordinal))
                     .ToList())
        {
            if (!wanted.Contains(block.Ip))
                RemoveBlockLocked(block, "Удалён из постоянного чёрного списка");
        }

        foreach (var ip in wanted)
        {
            if (_blocks.ContainsKey(ip)) continue;
            if (!IpMatcher.TryParseAddress(ip, out var address) || address is null) continue;
            CreateBlockLocked(address, 0, BlacklistReason, null, forcePermanent: true);
        }
    }

    private void ReconcileFirewallLocked()
    {
        var cleared = _firewall.ClearGroup();
        if (!cleared)
        {
            _firewallError = "Не удалось очистить старые блокировки.";
            _logger.Warn(_firewallError);
        }

        foreach (var block in _blocks.Values)
        {
            block.Simulated = _config.DryRun;
            block.Applied = false;
            if (block.Simulated) continue;
            ApplyFirewallLocked(block);
        }
    }

    private void OnSweepTimer()
    {
        try
        {
            Sweep(DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка фонового обслуживания: " + ex.Message);
        }
    }

    /// <summary>Снимает истёкшие блокировки и повторяет неприменённые правила.</summary>
    internal void Sweep(DateTime utcNow)
    {
        lock (_sync)
        {
            if (!_started) return;

            var changed = false;

            foreach (var block in _blocks.Values.ToList())
            {
                if (block.ExpiresAtUtc is DateTime expires && expires <= utcNow)
                {
                    RemoveBlockLocked(block, "Истёк срок блокировки");
                    changed = true;
                }
            }

            var retryDue = (utcNow - _lastFirewallRetryUtc).TotalSeconds >= FirewallRetrySeconds;
            if (!_config.DryRun && retryDue)
            {
                _lastFirewallRetryUtc = utcNow;
                foreach (var block in _blocks.Values.Where(b => !b.Simulated && !b.Applied).ToList())
                {
                    if (ApplyFirewallLocked(block))
                    {
                        _logger.Info($"Блокировка для {block.Ip} применено повторно.", block.Ip);
                        changed = true;
                    }
                }

                if (_blocks.Values.All(b => b.Simulated || b.Applied))
                    _firewallError = null;
            }

            _tracker.Prune(utcNow, TimeSpan.FromMinutes(_config.ObservationWindowMinutes));
            PruneOffenseCountsLocked();
            PruneTrustedRdpLocked(utcNow);

            if (changed || _stateDirty)
                SaveStateLocked();
        }
    }

    private void PruneTrustedRdpLocked(DateTime utcNow)
    {
        foreach (var ip in _trustedRdp.Where(pair => pair.Value <= utcNow).Select(pair => pair.Key).ToList())
            _trustedRdp.Remove(ip);
    }

    private void PruneOffenseCountsLocked()
    {
        if (_state.OffenseCounts.Count <= 20_000) return;

        var keep = _state.OffenseCounts
            .OrderByDescending(pair => pair.Value)
            .Take(10_000)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        _state.OffenseCounts = keep;
    }

    private void SaveStateLocked()
    {
        try
        {
            _state.Blocks = _blocks.Values.OrderBy(b => b.BlockedAtUtc).ToList();
            _stateStore.Save(_state);
            _stateDirty = false;
        }
        catch (Exception ex)
        {
            _logger.Error("Не удалось сохранить состояние: " + ex.Message);
        }
    }

    private static BlockedIpView ToView(BlockedIp block) => new()
    {
        Ip = block.Ip,
        BlockedAtUtc = block.BlockedAtUtc,
        ExpiresAtUtc = block.ExpiresAtUtc,
        Attempts = block.Attempts,
        Reason = block.Reason,
        BlockNumber = block.BlockNumber,
        Simulated = block.Simulated,
        Applied = block.Applied,
        Permanent = block.Permanent
    };

    private static string BuildRuleDescription(BlockedIp block) =>
        $"RDP Defender: {block.Reason}. Блокировка с {block.BlockedAtUtc:dd.MM.yyyy HH:mm} UTC, попыток: {block.Attempts}.";

    private static string DisplayUser(FailedLogonEvent e) =>
        string.IsNullOrWhiteSpace(e.UserName) ? "неизвестно" : e.UserName;

    public static string FormatDuration(int minutes)
    {
        if (minutes < 60) return $"{minutes} мин";
        if (minutes < 1440) return $"{minutes / 60.0:0.#} ч";
        return $"{minutes / 1440.0:0.#} сут";
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }

        try { Stop(); } catch { }
        try { _source.FailedLogon -= OnFailedLogon; } catch { }
        try { _rdp261Monitor.ConnectionObserved -= OnRdp261Observed; } catch { }
        try { _rdp261Monitor.SourceObserved -= OnRdp261SourceObserved; } catch { }
        try { _rdp261Monitor.Authenticated -= OnRdp1149Authenticated; } catch { }
        try { _gatewayMonitor.EventObserved -= OnRdpGatewayEvent; } catch { }
        try { _source.Dispose(); } catch { }
        try { _rdp261Monitor.Dispose(); } catch { }
        try { _gatewayMonitor.Dispose(); } catch { }
        try { _sweepTimer.Dispose(); } catch { }
        try { _firewall.Dispose(); } catch { }
        try { _logger.Dispose(); } catch { }
    }
}
