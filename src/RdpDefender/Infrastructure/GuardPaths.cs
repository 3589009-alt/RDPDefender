using System.Reflection;

namespace RdpDefender.Infrastructure;

/// <summary>
/// Пути данных приложения. По умолчанию — %ProgramData%\RDP Defender.
/// Переменная окружения RDPDEFENDER_DATA_DIR переопределяет каталог (полезно для тестов).
/// </summary>
public sealed class GuardPaths
{
    public const string ProductName = "RDP Defender";
    public const string ServiceName = "RDPDefender";
    public const string FirewallGroup = "RDP Defender";
    public const string PipeName = "RDPDefender.Control";

    public string DataDirectory { get; }
    public string ConfigFile => Path.Combine(DataDirectory, "config.json");
    public string StateFile => Path.Combine(DataDirectory, "state.json");
    public string LogDirectory => Path.Combine(DataDirectory, "logs");

    public GuardPaths(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
            throw new ArgumentException("Каталог данных не задан.", nameof(dataDirectory));
        DataDirectory = Path.GetFullPath(dataDirectory);
    }

    public static GuardPaths CreateDefault()
    {
        var overrideDir = Environment.GetEnvironmentVariable("RDPDEFENDER_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir))
            return new GuardPaths(overrideDir);

        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            ProductName);
        return new GuardPaths(root);
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    public static string ExecutablePath
    {
        get
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(processPath) &&
                !Path.GetFileName(processPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                return processPath;
            }

            var entryName = Assembly.GetEntryAssembly()?.GetName().Name ?? "RdpDefender";
            return Path.Combine(AppContext.BaseDirectory, entryName + ".exe");
        }
    }

    public static string Version
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? "1.3.2" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }
}
