using System.Net;
using RdpDefender.Core;
using RdpDefender.Infrastructure;

namespace RdpDefender.Ui;

public sealed partial class MainForm
{
    private readonly HashSet<string> _countryPending = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _countrySync = new();

    private string CountryPlaceholder(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address) || address is null)
            return "—";

        if (WhoisLookup.IsNonPublic(address))
            return "LAN";

        return _whoisLookup.TryGetCached(ip, out var cached)
            ? FormatCountry(cached)
            : "…";
    }

    private void EnrichVisibleCountries(DarkGrid grid)
    {
        if (grid.IsDisposed || grid.Rows.Count == 0) return;

        var first = 0;
        try { first = Math.Max(0, grid.FirstDisplayedScrollingRowIndex); }
        catch { }

        var count = 0;
        try { count = grid.DisplayedRowCount(false); }
        catch { }
        if (count <= 0) count = Math.Min(grid.Rows.Count, 20);

        var last = Math.Min(grid.Rows.Count, first + count);
        for (var i = first; i < last; i++)
        {
            var ip = IpOf(grid.Rows[i].Tag);
            if (!string.IsNullOrWhiteSpace(ip))
                EnsureCountry(ip);
        }
    }

    private void EnsureCountry(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address) || address is null) return;

        if (WhoisLookup.IsNonPublic(address))
        {
            UpdateCountryCells(ip, "LAN");
            return;
        }

        if (_whoisLookup.TryGetCached(ip, out var cached))
        {
            UpdateCountryCells(ip, FormatCountry(cached));
            return;
        }

        lock (_countrySync)
        {
            if (!_countryPending.Add(ip)) return;
        }

        _ = LookupCountryAsync(ip);
    }

    private async Task LookupCountryAsync(string ip)
    {
        try
        {
            var lookup = await _whoisLookup.LookupAsync(ip);
            UpdateCountryCells(ip, FormatCountry(lookup));
        }
        catch
        {
            UpdateCountryCells(ip, "—");
        }
        finally
        {
            lock (_countrySync) _countryPending.Remove(ip);
        }
    }

    private void UpdateCountryCells(string ip, string country)
    {
        if (IsDisposed) return;

        foreach (var grid in new[] { _blocksGrid, _attemptsGrid, _successGrid })
        {
            if (grid is null || grid.IsDisposed) continue;

            var column = grid.Columns["country"];
            if (column is null) continue;

            foreach (DataGridViewRow row in grid.Rows)
            {
                if (!string.Equals(IpOf(row.Tag), ip, StringComparison.OrdinalIgnoreCase))
                    continue;
                row.Cells[column.Index].Value = country;
            }
        }
    }

    private static string IpOf(object? tag) => tag switch
    {
        BlockedIpView block => block.Ip,
        FailedAttemptView attempt => attempt.Ip,
        SuccessfulLogonView success => success.Ip,
        _ => ""
    };

    private static string FormatCountry(AttackerLookup? lookup)
    {
        if (lookup is null) return "—";
        if (IPAddress.TryParse(lookup.Ip, out var address) && address is not null &&
            WhoisLookup.IsNonPublic(address))
            return "LAN";
        if (!string.IsNullOrWhiteSpace(lookup.Country)) return lookup.Country;
        return "—";
    }
}
