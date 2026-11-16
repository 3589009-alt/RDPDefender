using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace RdpDefender.Infrastructure;

/// <summary>Сведения об IP, полученные из обратного DNS и whois.</summary>
public sealed class AttackerLookup
{
    public string Ip { get; init; } = "";
    public string Ptr { get; set; } = "";
    public string Country { get; set; } = "";
    public string Network { get; set; } = "";
    public string Organization { get; set; } = "";
    public string Asn { get; set; } = "";
    public string Abuse { get; set; } = "";
    public string RawWhois { get; set; } = "";
    public string Error { get; set; } = "";
    public DateTime LookedUpUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Асинхронный whois-клиент с кэшем. Использует TCP/43 и referral-сервер IANA.
/// </summary>
public sealed class WhoisLookup
{
    private sealed record CacheItem(AttackerLookup Value, DateTime ExpiresUtc);

    private static readonly TimeSpan SuccessTtl = TimeSpan.FromHours(12);
    private static readonly TimeSpan ErrorTtl = TimeSpan.FromMinutes(15);
    private static readonly Regex ServerPattern = new("^[a-z0-9.-]+$", RegexOptions.IgnoreCase);

    private readonly ConcurrentDictionary<string, CacheItem> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(2, 2);

    public async Task<AttackerLookup> LookupAsync(string ip, CancellationToken cancellationToken = default, bool refresh = false)
    {
        var key = (ip ?? "").Trim();
        if (!IPAddress.TryParse(key, out var address) || address is null)
        {
            return new AttackerLookup { Ip = key, Error = "Некорректный IP-адрес." };
        }

        if (IsNonPublic(address))
        {
            return new AttackerLookup
            {
                Ip = key,
                Error = "Локальный или служебный адрес — whois не требуется."
            };
        }

        if (!refresh && _cache.TryGetValue(key, out var cached) && cached.ExpiresUtc > DateTime.UtcNow)
            return Clone(cached.Value);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!refresh && _cache.TryGetValue(key, out cached) && cached.ExpiresUtc > DateTime.UtcNow)
                return Clone(cached.Value);

            var result = new AttackerLookup { Ip = key };

            try
            {
                var ptr = await Dns.GetHostEntryAsync(address)
                    .WaitAsync(TimeSpan.FromSeconds(3), cancellationToken)
                    .ConfigureAwait(false);
                result.Ptr = ptr.HostName;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // PTR не обязателен.
            }

            var raw = new StringBuilder();
            var iana = "";
            var referral = "";
            try
            {
                iana = await QueryAsync("whois.iana.org", key, TimeSpan.FromSeconds(5), cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(iana))
                    raw.AppendLine("=== whois.iana.org ===").AppendLine(iana.Trim()).AppendLine();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Error = $"whois.iana.org: {ex.Message}";
            }

            var server = "whois.arin.net";
            var match = Regex.Match(iana, "(?im)^\\s*(?:refer|whois):\\s*(\\S+)");
            if (match.Success && ServerPattern.IsMatch(match.Groups[1].Value.Trim()))
                server = match.Groups[1].Value.Trim().ToLowerInvariant();

            try
            {
                referral = await QueryAsync(server, key, TimeSpan.FromSeconds(10), cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(referral))
                    raw.AppendLine($"=== {server} ===").AppendLine(referral.Trim());
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var message = $"whois {server}: {ex.Message}";
                result.Error = string.IsNullOrWhiteSpace(result.Error) ? message : result.Error + " · " + message;
            }

            // ARIN часто отдаёт только широкий legacy-блок и ссылку на RIPE/APNIC.
            // Догоняем второй referral, чтобы получить реальную сеть и организацию.
            if (!string.IsNullOrWhiteSpace(referral))
            {
                var secondMatch = Regex.Match(
                    referral,
                    "(?im)^\\s*(?:refer|whois|referralserver):\\s*(?:whois://)?([a-z0-9.-]+)");
                if (secondMatch.Success)
                {
                    var secondServer = secondMatch.Groups[1].Value.Trim().ToLowerInvariant();
                    if (ServerPattern.IsMatch(secondServer) &&
                        !secondServer.Equals(server, StringComparison.OrdinalIgnoreCase) &&
                        !secondServer.Equals("whois.iana.org", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var second = await QueryAsync(secondServer, key, TimeSpan.FromSeconds(10), cancellationToken)
                                .ConfigureAwait(false);
                            if (!string.IsNullOrWhiteSpace(second))
                            {
                                raw.AppendLine($"=== {secondServer} ===").AppendLine(second.Trim());
                                referral = second;
                            }
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            var message = $"whois {secondServer}: {ex.Message}";
                            result.Error = string.IsNullOrWhiteSpace(result.Error) ? message : result.Error + " · " + message;
                        }
                    }
                }
            }

            var text = string.IsNullOrWhiteSpace(referral) ? iana : referral;
            result.RawWhois = raw.ToString().Trim();
            result.Country = NormalizeCountry(FindField(text, "country", "countrycode", "country-code"));
            result.Network = FindField(text, "inetnum", "inet6num", "netrange", "cidr", "route", "route6", "netname");
            result.Organization = FindField(text, "org-name", "orgname", "organization", "owner", "descr", "org");
            result.Asn = FindField(text, "origin", "originas", "aut-num", "asn");
            result.Abuse = FindField(text, "abuse-mailbox", "orgabuseemail", "abuse-c", "e-mail");

            if (string.IsNullOrWhiteSpace(result.RawWhois))
            {
                result.Error = string.IsNullOrWhiteSpace(result.Error)
                    ? "Whois не вернул данных."
                    : result.Error;
            }

            result.LookedUpUtc = DateTime.UtcNow;
            var ttl = string.IsNullOrWhiteSpace(result.RawWhois) ? ErrorTtl : SuccessTtl;
            _cache[key] = new CacheItem(Clone(result), DateTime.UtcNow + ttl);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public static bool IsNonPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            if (b[0] is 0 or 10 or 127) return true;
            if (b[0] == 100 && b[1] is >= 64 and <= 127) return true;
            if (b[0] == 169 && b[1] == 254) return true;
            if (b[0] == 172 && b[1] is >= 16 and <= 31) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            if (b[0] == 192 && b[1] == 0 && b[2] == 0) return true;
            if (b[0] == 192 && b[1] == 0 && b[2] == 2) return true;
            if (b[0] == 198 && b[1] is 18 or 19) return true;
            if (b[0] == 198 && b[1] == 51 && b[2] == 100) return true;
            if (b[0] == 203 && b[1] == 0 && b[2] == 113) return true;
            if (b[0] >= 224) return true;
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.Equals(IPAddress.IPv6Loopback) ||
                address.Equals(IPAddress.IPv6Any) ||
                address.IsIPv6LinkLocal ||
                address.IsIPv6SiteLocal ||
                address.IsIPv6Multicast)
                return true;

            var b = address.GetAddressBytes();
            return (b[0] & 0xFE) == 0xFC; // fc00::/7
        }

        return true;
    }

    private static async Task<string> QueryAsync(
        string server,
        string query,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!ServerPattern.IsMatch(server))
            throw new InvalidOperationException("Некорректный whois-сервер.");

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);

        using var client = new TcpClient();
        await client.ConnectAsync(server, 43, linked.Token).ConfigureAwait(false);
        await using var stream = client.GetStream();

        var request = Encoding.ASCII.GetBytes(query + "\r\n");
        await stream.WriteAsync(request, linked.Token).ConfigureAwait(false);

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, linked.Token).ConfigureAwait(false)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length >= 256 * 1024) break;
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string FindField(string text, params string[] names)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        foreach (var wanted in names)
        {
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('%') || trimmed.StartsWith('#')) continue;
                var colon = trimmed.IndexOf(':');
                if (colon <= 0) continue;

                var name = trimmed[..colon].Trim();
                if (!wanted.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;

                var value = trimmed[(colon + 1)..].Trim();
                if (value.Length > 0 && !value.Equals("null", StringComparison.OrdinalIgnoreCase))
                    return value;
            }
        }
        return "";
    }

    private static string NormalizeCountry(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return "";
        if (trimmed.Length > 3)
            return trimmed;
        return trimmed.ToUpperInvariant();
    }

    private static AttackerLookup Clone(AttackerLookup value) => new()
    {
        Ip = value.Ip,
        Ptr = value.Ptr,
        Country = value.Country,
        Network = value.Network,
        Organization = value.Organization,
        Asn = value.Asn,
        Abuse = value.Abuse,
        RawWhois = value.RawWhois,
        Error = value.Error,
        LookedUpUtc = value.LookedUpUtc
    };
}
