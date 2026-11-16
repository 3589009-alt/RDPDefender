using System.ServiceProcess;
using RdpDefender.Infrastructure;
using RdpDefender.Service;
using RdpDefender.Tools;
using ServiceInstaller = RdpDefender.Service.ServiceInstaller;

namespace RdpDefender;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var options = CommandLineOptions.Parse(args);

        if (options.Help)
        {
            ConsoleSupport.AttachToParent();
            Console.WriteLine(CommandLineOptions.Usage);
            return 0;
        }

        if (options.SelfTest)
        {
            ConsoleSupport.AttachToParent();
            return SelfTest.Run(options.SelfTestReport);
        }

        if (options.Simulate)
        {
            ConsoleSupport.AttachToParent();
            return Simulation.Run();
        }

        if (options.UiPreview is not null)
            return UiPreview.Run(options.UiPreview);

        if (options.Install)
        {
            ConsoleSupport.AttachToParent();
            try
            {
                ServiceInstaller.Install(GuardPaths.ExecutablePath, start: !options.NoStart);
                Console.WriteLine(options.NoStart
                    ? "Служба RDP Defender установлена. Запуск отложен."
                    : "Служба RDP Defender установлена и запущена.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Не удалось установить службу: " + ex.Message);
                return 1;
            }
        }

        if (options.Uninstall)
        {
            ConsoleSupport.AttachToParent();
            try
            {
                ServiceInstaller.Uninstall(removeData: options.Purge);
                Console.WriteLine(options.Purge
                    ? "Служба и данные RDP Defender удалены."
                    : "Служба RDP Defender удалена. Данные сохранены.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Не удалось удалить службу: " + ex.Message);
                return 1;
            }
        }

        if (options.Service)
        {
            ServiceBase.Run(new GuardService());
            return 0;
        }

        if (options.ConsoleMode)
            return ConsoleRunner.Run();

        return RunGui();
    }

    private static int RunGui()
    {
        using var mutex = new Mutex(true, @"Local\RDPDefender.Gui", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "RDP Defender уже запущен. Откройте окно из значка в трее.",
                "RDP Defender",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 0;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.ThreadException += (_, e) =>
        {
            LogCrash(e.Exception);
            try
            {
                Ui.UiDialog.Error(null, "Непредвиденная ошибка",
                    e.Exception.Message + "\n\nПодробности записаны в logs\\crash.log.");
            }
            catch { }
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
                LogCrash(exception);
        };

        Application.AddMessageFilter(new Ui.MouseWheelRouter());
        Application.Run(new Ui.MainForm());
        return 0;
    }

    private static void LogCrash(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                GuardPaths.ProductName,
                "logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Не допускаем рекурсию при сбое самого обработчика.
        }
    }
}
