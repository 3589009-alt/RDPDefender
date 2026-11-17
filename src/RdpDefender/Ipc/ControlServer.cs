using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using RdpDefender.Core;
using RdpDefender.Infrastructure;

namespace RdpDefender.Ipc;

/// <summary>
/// Локальный канал управления (named pipe). Доступ разрешён только
/// SYSTEM и членам группы администраторов.
/// </summary>
public sealed class ControlServer : IDisposable
{
    private readonly GuardEngine _engine;
    private readonly string _pipeName;
    private readonly Logger _logger;
    private readonly PipeSecurity? _security;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private bool _disposed;

    public ControlServer(GuardEngine engine, string pipeName, Logger logger, PipeSecurity? security = null)
    {
        _engine = engine;
        _pipeName = pipeName;
        _logger = logger;
        _security = security;
    }

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        _logger.Info($"Канал управления «{_pipeName}» запущен.");
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                var connected = pipe;
                pipe = null;
                _ = Task.Run(() => HandleClientAsync(connected), CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Warn("Канал управления: " + ex.Message);
                try { await Task.Delay(1000, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
            finally
            {
                pipe?.Dispose();
            }
        }
    }

    private NamedPipeServerStream CreatePipe()
    {
        try
        {
            var security = _security ?? CreateDefaultSecurity();

            return NamedPipeServerStreamAcl.Create(
                _pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 4,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                inBufferSize: 0,
                outBufferSize: 0,
                pipeSecurity: security);
        }
        catch (Exception ex)
        {
            _logger.Error($"Не удалось задать ACL канала управления ({ex.Message}). Канал будет доступен только SYSTEM.");
            return new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 4,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        }
    }

    private static PipeSecurity CreateDefaultSecurity()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        return security;
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe)
    {
        using (pipe)
        {
            try
            {
                using var reader = new StreamReader(
                    pipe, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, 4096, leaveOpen: true);
                using var writer = new StreamWriter(
                    pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };

                var line = await reader.ReadLineAsync()
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(line)) return;

                ControlRequest? request;
                try
                {
                    request = JsonSerializer.Deserialize<ControlRequest>(line, JsonDefaults.Options);
                }
                catch (Exception ex)
                {
                    await WriteAsync(writer, ControlResponse.Failure("Некорректный запрос: " + ex.Message));
                    return;
                }

                if (request is null) return;
                var response = Execute(request);
                await WriteAsync(writer, response);
            }
            catch (TimeoutException)
            {
                _logger.Debug("Канал управления: таймаут чтения запроса.");
            }
            catch (IOException)
            {
                // Клиент отключился — штатная ситуация.
            }
            catch (Exception ex)
            {
                _logger.Warn("Канал управления: " + ex.Message);
            }
        }
    }

    private static async Task WriteAsync(StreamWriter writer, ControlResponse response)
    {
        var json = JsonSerializer.Serialize(response, JsonDefaults.Compact);
        await writer.WriteLineAsync(json).ConfigureAwait(false);
    }

    private ControlResponse Execute(ControlRequest request)
    {
        try
        {
            switch ((request.Cmd ?? "").Trim().ToLowerInvariant())
            {
                case "ping":
                    return ControlResponse.Success(new { pong = true, version = GuardPaths.Version });

                case "clearhistory":
                    _engine.ClearHistory(request.Reason ?? "Очистка списков");
                    return ControlResponse.Success(_engine.GetStatus(50));

                case "status":
                    return ControlResponse.Success(
                        _engine.GetStatus(request.Count ?? 200, request.AttemptsCount ?? 50));

                case "unblock":
                {
                    if (string.IsNullOrWhiteSpace(request.Ip))
                        return ControlResponse.Failure("Не указан IP-адрес.");
                    var ok = _engine.Unblock(request.Ip, request.Reason ?? "Разблокирован вручную");
                    return ok
                        ? ControlResponse.Success(new { unblocked = true })
                        : ControlResponse.Failure("IP-адрес не найден в списке блокировок.");
                }

                case "unblockall":
                {
                    var removed = _engine.UnblockAll(request.Reason ?? "Все блокировки сняты");
                    return ControlResponse.Success(new { removed });
                }

                case "block":
                {
                    if (string.IsNullOrWhiteSpace(request.Ip))
                        return ControlResponse.Failure("Не указан IP-адрес.");
                    _engine.Block(request.Ip, request.Minutes, request.Reason);
                    return ControlResponse.Success(new { blocked = true });
                }

                case "setenabled":
                {
                    if (request.Enabled is null)
                        return ControlResponse.Failure("Не указано состояние защиты.");
                    _engine.SetEnabled(request.Enabled.Value);
                    return ControlResponse.Success(_engine.GetStatus(50));
                }

                case "saveconfig":
                {
                    if (request.Config is null)
                        return ControlResponse.Failure("Настройки не переданы.");
                    _engine.ApplyConfig(request.Config);
                    return ControlResponse.Success(_engine.GetStatus(50));
                }

                case "reload":
                    _engine.ReloadConfig();
                    return ControlResponse.Success(_engine.GetStatus(50));

                default:
                    return ControlResponse.Failure("Неизвестная команда.");
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Команда «{request.Cmd}» не выполнена: {ex.Message}");
            return ControlResponse.Failure(ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _cts?.Cancel(); } catch { }
        try { _acceptTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        try { _cts?.Dispose(); } catch { }
        _cts = null;
    }
}
