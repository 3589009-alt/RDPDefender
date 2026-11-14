using System.ServiceProcess;
using RdpDefender.Configuration;
using RdpDefender.Core;
using RdpDefender.Firewall;
using RdpDefender.Infrastructure;
using RdpDefender.Ipc;
using RdpDefender.Security;

namespace RdpDefender.Service;

public sealed class GuardService : ServiceBase
{
    private GuardEngine? _engine;
    private ControlServer? _control;
    private Logger? _logger;

    public GuardService()
    {
        ServiceName = ServiceInstaller.ServiceName;
        CanStop = true;
        CanShutdown = true;
        AutoLog = false;
    }

    protected override void OnStart(string[] args)
    {
        GuardPaths? paths = null;
        try
        {
            paths = GuardPaths.CreateDefault();
            paths.EnsureCreated();
            _logger = new Logger(paths.LogDirectory);
            _logger.Info($"Запуск службы RDP Defender {GuardPaths.Version}.");

            var configStore = new ConfigStore(paths.ConfigFile);
            var config = configStore.Load(out var warning);
            if (!string.IsNullOrEmpty(warning))
                _logger.Warn(warning);

            var stateStore = new StateStore(paths.StateFile);
            var firewall = BlockerFactory.Create(config, _logger);
            var source = new SecurityEventMonitor(_logger);

            _engine = new GuardEngine(config, configStore, stateStore, _logger, firewall, source, paths);
            _engine.Start();

            _control = new ControlServer(_engine, config.PipeName, _logger);
            _control.Start();

            _logger.Info("Служба RDP Defender запущена.");
        }
        catch (Exception ex)
        {
            _logger?.Error("Критическая ошибка запуска службы: " + ex);
            try { _control?.Dispose(); } catch { }
            try { _engine?.Dispose(); } catch { }
            _logger?.Dispose();
            throw;
        }
    }

    protected override void OnStop()
    {
        try
        {
            _logger?.Info("Остановка службы RDP Defender.");
            _control?.Dispose();
            _engine?.Dispose();
        }
        catch (Exception ex)
        {
            _logger?.Error("Ошибка остановки службы: " + ex.Message);
        }
        finally
        {
            _logger?.Dispose();
            _control = null;
            _engine = null;
            _logger = null;
        }
    }

    protected override void OnShutdown() => OnStop();
}
