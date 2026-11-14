using System.Reflection;
using System.Runtime.InteropServices;
using RdpDefender.Infrastructure;

namespace RdpDefender.Firewall;

/// <summary>
/// Подготавливает WinDivert рядом с исполняемым файлом.
/// Официальные подписанные WinDivert.dll и WinDivert64.sys встроены в exe,
/// поэтому одного файла достаточно для первого запуска.
/// </summary>
public static class WinDivertRuntime
{
    public const string Version = "2.2.2";

    private const int ExpectedDllLength = 47_616;
    private const int ExpectedDriverLength = 94_144;

    private const string DllResource = "RdpDefender.Assets.WinDivert.dll";
    private const string DriverResource = "RdpDefender.Assets.WinDivert64.sys";
    private const string LicenseResource = "RdpDefender.Assets.LICENSE-WinDivert.txt";

    private static readonly object Sync = new();
    private static bool _prepared;

    public static string ApplicationDirectory => AppContext.BaseDirectory;

    public static string DllPath => Path.Combine(ApplicationDirectory, "WinDivert.dll");
    public static string DriverPath => Path.Combine(ApplicationDirectory, "WinDivert64.sys");
    public static string LicensePath => Path.Combine(ApplicationDirectory, "LICENSE-WinDivert.txt");

    public static void EnsureExtracted(Logger logger)
    {
        lock (Sync)
        {
            if (_prepared) return;

            if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
                throw new PlatformNotSupportedException("WinDivert в этой сборке поддерживает только x64.");

            try
            {
                ExtractIfNeeded(DllResource, DllPath, ExpectedDllLength);
                ExtractIfNeeded(DriverResource, DriverPath, ExpectedDriverLength);

                if (!File.Exists(LicensePath))
                    ExtractResource(LicenseResource, LicensePath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Не удалось подготовить WinDivert рядом с программой. " +
                    "Запустите RDP Defender от администратора или скопируйте WinDivert.dll, " +
                    "WinDivert64.sys и LICENSE-WinDivert.txt в папку программы. " + ex.Message, ex);
            }

            _prepared = true;
            logger.Info($"WinDivert {Version}: компоненты подготовлены в {ApplicationDirectory}");
        }
    }

    /// <summary>Проверяет синтаксис фильтра без установки драйвера.</summary>
    public static string? ValidateFilter(string filter)
    {
        var objectBuffer = Marshal.AllocHGlobal(64 * 1024);
        try
        {
            var ok = WinDivertNative.WinDivertHelperCompileFilter(
                filter,
                WinDivertNative.LayerNetwork,
                objectBuffer,
                64 * 1024,
                out var errorString,
                out var errorPosition);

            if (ok) return null;

            var message = errorString == IntPtr.Zero
                ? "неизвестная ошибка разбора фильтра"
                : Marshal.PtrToStringAnsi(errorString) ?? "неизвестная ошибка разбора фильтра";

            return $"{message} (позиция {errorPosition})";
        }
        finally
        {
            Marshal.FreeHGlobal(objectBuffer);
        }
    }

    private static void ExtractIfNeeded(string resourceName, string path, int expectedLength)
    {
        if (File.Exists(path) && new FileInfo(path).Length == expectedLength)
            return;

        ExtractResource(resourceName, path);
    }

    private static void ExtractResource(string resourceName, string path)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Встроенный ресурс {resourceName} не найден.");

        var temp = path + ".tmp";
        using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.CopyTo(file);
        }

        File.Move(temp, path, overwrite: true);
    }
}
