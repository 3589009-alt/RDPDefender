using System.Text.Json;
using RdpDefender.Infrastructure;

namespace RdpDefender.Configuration;

public sealed class ConfigStore
{
    public string Path { get; }

    public ConfigStore(string path)
    {
        Path = path;
    }

    public GuardConfig Load(out string? warning)
    {
        warning = null;
        var warnings = new List<string>();

        try
        {
            if (!File.Exists(Path))
            {
                var fresh = new GuardConfig();
                fresh.Normalize();
                Save(fresh);
                warning = "Файл настроек не найден — созданы значения по умолчанию.";
                return fresh;
            }

            var json = File.ReadAllText(Path);
            var config = JsonSerializer.Deserialize<GuardConfig>(json, JsonDefaults.Options);
            if (config is null)
                throw new InvalidDataException("Пустой файл настроек.");

            var migrated = false;
            if (config.ConfigVersion < GuardConfig.CurrentConfigVersion)
            {
                var added = config.MergeDefaultLocalNetworks();
                config.ConfigVersion = GuardConfig.CurrentConfigVersion;
                migrated = true;

                if (added)
                    warnings.Add("в белый список добавлены локальные сети (RFC1918, loopback, link-local, ULA)");
            }

            config.Normalize();
            var errors = config.Validate();
            if (errors.Count > 0)
            {
                warnings.Add("настройки содержали ошибки, применены значения по умолчанию: " + string.Join("; ", errors));
                config = new GuardConfig();
                config.Normalize();
                Save(config);
            }
            else if (migrated)
            {
                Save(config);
            }

            warning = warnings.Count == 0
                ? null
                : char.ToUpperInvariant(warnings[0][0]) + warnings[0][1..] +
                  (warnings.Count > 1 ? " " + string.Join(" ", warnings.Skip(1)) : "");
            return config;
        }
        catch (Exception ex)
        {
            warning = $"Не удалось прочитать настройки ({ex.Message}). Созданы значения по умолчанию.";
            TryBackupBrokenFile();
            var fallback = new GuardConfig();
            fallback.Normalize();
            try { Save(fallback); } catch { }
            return fallback;
        }
    }

    public void Save(GuardConfig config)
    {
        config.Normalize();
        config.ConfigVersion = GuardConfig.CurrentConfigVersion;

        var dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(config, JsonDefaults.Options);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, Path, overwrite: true);
    }

    private void TryBackupBrokenFile()
    {
        try
        {
            if (!File.Exists(Path)) return;
            var backup = Path + $".bad-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(Path, backup, overwrite: true);
        }
        catch { }
    }
}
