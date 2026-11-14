using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace RdpDefender.Infrastructure;

public sealed record UpdateInfo(
    Version Version,
    string Tag,
    string SetupAssetName,
    string SetupUrl,
    string Sha256Url,
    string ReleaseUrl,
    string Notes);

/// <summary>
/// Проверка и загрузка обновлений из GitHub Releases.
/// Установщик проверяется по SHA-256 из SHA256SUMS.txt.
/// </summary>
public static class UpdateService
{
    public const string ApiUrl = "https://api.github.com/repos/3589009-alt/RDPDefender/releases/latest";
    private static readonly HttpClient Client = CreateClient();

    public static Version CurrentVersion
    {
        get
        {
            var raw = GuardPaths.Version;
            return Version.TryParse(raw, out var version) ? version : new Version(1, 0, 0);
        }
    }

    public static async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(ApiUrl, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub вернул HTTP {(int)response.StatusCode}.");

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var root = json.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() ?? "" : "";
        var page = root.TryGetProperty("html_url", out var pageElement) ? pageElement.GetString() ?? "" : "";
        var notes = root.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() ?? "" : "";
        var version = ParseTag(tag);
        if (version is null || version <= current) return null;

        string? setupName = null;
        string? setupUrl = null;
        string? shaUrl = null;

        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? "" : "";
                var url = asset.TryGetProperty("browser_download_url", out var urlElement)
                    ? urlElement.GetString() ?? ""
                    : "";

                if (name.StartsWith("RDP-Defender-Setup-", StringComparison.OrdinalIgnoreCase) &&
                    name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    setupName = name;
                    setupUrl = url;
                }
                else if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                {
                    shaUrl = url;
                }
            }
        }

        if (setupName is null || setupUrl is null || shaUrl is null)
            throw new InvalidOperationException("В релизе нет установщика или файла SHA256SUMS.txt.");

        return new UpdateInfo(version, tag, setupName, setupUrl, shaUrl, page, notes);
    }

    public static async Task<string> DownloadAndVerifyAsync(
        UpdateInfo update,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RDP Defender Update");
        Directory.CreateDirectory(directory);
        var setupPath = Path.Combine(directory, update.SetupAssetName);

        try { if (File.Exists(setupPath)) File.Delete(setupPath); } catch { }

        using (var response = await Client.GetAsync(
                   update.SetupUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Не удалось скачать установщик: HTTP {(int)response.StatusCode}.");

            var total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var target = File.Create(setupPath);

            var buffer = new byte[128 * 1024];
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                received += read;
                if (total is > 0)
                    progress?.Report((int)Math.Clamp(received * 100 / total.Value, 0, 99));
            }
        }

        var shaText = await Client.GetStringAsync(update.Sha256Url, cancellationToken).ConfigureAwait(false);
        var expected = ParseChecksum(shaText, update.SetupAssetName);
        if (expected is null)
            throw new InvalidOperationException("Не найдена контрольная сумма установщика.");

        await using (var file = File.OpenRead(setupPath))
        {
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken)
                .ConfigureAwait(false));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(setupPath); } catch { }
                throw new InvalidOperationException("SHA-256 установщика не совпадает.");
            }
        }

        progress?.Report(100);
        return setupPath;
    }

    public static void LaunchInstaller(string setupPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = setupPath,
            WorkingDirectory = Path.GetDirectoryName(setupPath) ?? Path.GetTempPath(),
            UseShellExecute = true
        });
    }

    public static Version? ParseTag(string tag)
    {
        var value = (tag ?? "").Trim();
        if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            value = value[1..];

        var dash = value.IndexOf('-');
        if (dash >= 0) value = value[..dash];

        return Version.TryParse(value, out var version) ? version : null;
    }

    public static string? ParseChecksum(string shaText, string assetName)
    {
        foreach (var line in shaText.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            if (!parts[^1].Equals(assetName, StringComparison.OrdinalIgnoreCase)) continue;
            if (parts[0].Length != 64) continue;
            return parts[0];
        }

        return null;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "RDPDefender/" + GuardPaths.Version + " (+https://github.com/3589009-alt/RDPDefender)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
