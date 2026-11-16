using RdpDefender.Configuration;
using RdpDefender.Core;

namespace RdpDefender.Ui;

public sealed partial class MainForm
{
    private void ShowSelectedBlockDetails()
    {
        if (_blocksDetails is null) return;

        var block = SelectedBlocked().FirstOrDefault();
        if (block is null)
        {
            CancelBlockWhois();
            _blocksDetails.Clear("Выберите блокировку в списке — здесь появятся данные IP и WHOIS.");
            return;
        }

        var attempts = _status?.RecentAttempts
            .Where(a => string.Equals(a.Ip, block.Ip, StringComparison.OrdinalIgnoreCase))
            .ToList() ?? new List<FailedAttemptView>();

        _blocksDetails.ShowBlock(block, attempts);
        _ = LoadBlockWhoisAsync(block.Ip, refresh: false);
    }

    private void ShowSelectedAttemptDetails()
    {
        if (_attemptsDetails is null) return;

        var attempt = _attemptsGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => row.Tag)
            .OfType<FailedAttemptView>()
            .FirstOrDefault();

        if (attempt is null)
        {
            CancelAttemptWhois();
            _attemptsDetails.Clear("Выберите попытку входа в списке — здесь появятся данные IP и WHOIS.");
            return;
        }

        var sameIp = _status?.RecentAttempts
            .Where(a => string.Equals(a.Ip, attempt.Ip, StringComparison.OrdinalIgnoreCase))
            .ToList() ?? new List<FailedAttemptView>();
        var block = _status?.Blocked.FirstOrDefault(b =>
            string.Equals(b.Ip, attempt.Ip, StringComparison.OrdinalIgnoreCase));

        _attemptsDetails.ShowAttempt(attempt, sameIp, block);
        _ = LoadAttemptWhoisAsync(attempt.Ip, refresh: false);
    }

    private void ShowSelectedSuccessDetails()
    {
        if (_successDetails is null) return;

        var success = _successGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => row.Tag)
            .OfType<SuccessfulLogonView>()
            .FirstOrDefault();

        if (success is null)
        {
            CancelSuccessWhois();
            _successDetails.Clear("Выберите успешное подключение — здесь появятся данные IP и WHOIS.");
            return;
        }

        var attempts = _status?.RecentAttempts
            .Where(a => string.Equals(a.Ip, success.Ip, StringComparison.OrdinalIgnoreCase))
            .ToList() ?? new List<FailedAttemptView>();
        var block = _status?.Blocked.FirstOrDefault(b =>
            string.Equals(b.Ip, success.Ip, StringComparison.OrdinalIgnoreCase));
        var config = _status?.Config ?? new GuardConfig();

        var address = System.Net.IPAddress.TryParse(success.Ip, out var parsed) ? parsed : null;
        var blacklisted = address is not null &&
                          IpMatcher.IsMatch(address, config.PermanentBlocklist, out _);
        var whitelisted = address is not null &&
                          IpMatcher.IsMatch(address, config.Whitelist, out _);

        _successDetails.ShowSuccess(
            success,
            attempts,
            block,
            config.WhitelistSuccessfulLogins,
            whitelisted,
            blacklisted);
        _ = LoadSuccessWhoisAsync(success.Ip, refresh: false);
    }

    private async Task LoadBlockWhoisAsync(string ip, bool refresh)
    {
        if (string.IsNullOrWhiteSpace(ip) || _blocksDetails is null) return;

        _blocksWhoisCts?.Cancel();
        _blocksWhoisCts?.Dispose();
        var cts = new CancellationTokenSource();
        _blocksWhoisCts = cts;

        try
        {
            var lookup = await _whoisLookup.LookupAsync(ip, cts.Token, refresh);
            if (!cts.IsCancellationRequested &&
                string.Equals(_blocksDetails.Ip, ip, StringComparison.OrdinalIgnoreCase))
            {
                _blocksDetails.SetWhois(lookup);
                UpdateCountryCells(ip, FormatCountry(lookup));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested &&
                string.Equals(_blocksDetails.Ip, ip, StringComparison.OrdinalIgnoreCase))
            {
                _blocksDetails.SetWhoisError(ex.Message);
            }
        }
    }

    private async Task LoadAttemptWhoisAsync(string ip, bool refresh)
    {
        if (string.IsNullOrWhiteSpace(ip) || _attemptsDetails is null) return;

        _attemptsWhoisCts?.Cancel();
        _attemptsWhoisCts?.Dispose();
        var cts = new CancellationTokenSource();
        _attemptsWhoisCts = cts;

        try
        {
            var lookup = await _whoisLookup.LookupAsync(ip, cts.Token, refresh);
            if (!cts.IsCancellationRequested &&
                string.Equals(_attemptsDetails.Ip, ip, StringComparison.OrdinalIgnoreCase))
            {
                _attemptsDetails.SetWhois(lookup);
                UpdateCountryCells(ip, FormatCountry(lookup));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested &&
                string.Equals(_attemptsDetails.Ip, ip, StringComparison.OrdinalIgnoreCase))
            {
                _attemptsDetails.SetWhoisError(ex.Message);
            }
        }
    }

    private async Task LoadSuccessWhoisAsync(string ip, bool refresh)
    {
        if (string.IsNullOrWhiteSpace(ip) || _successDetails is null) return;

        _successWhoisCts?.Cancel();
        _successWhoisCts?.Dispose();
        var cts = new CancellationTokenSource();
        _successWhoisCts = cts;

        try
        {
            var lookup = await _whoisLookup.LookupAsync(ip, cts.Token, refresh);
            if (!cts.IsCancellationRequested &&
                string.Equals(_successDetails.Ip, ip, StringComparison.OrdinalIgnoreCase))
            {
                _successDetails.SetWhois(lookup);
                UpdateCountryCells(ip, FormatCountry(lookup));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested &&
                string.Equals(_successDetails.Ip, ip, StringComparison.OrdinalIgnoreCase))
            {
                _successDetails.SetWhoisError(ex.Message);
            }
        }
    }

    private void CancelBlockWhois()
    {
        _blocksWhoisCts?.Cancel();
        _blocksWhoisCts?.Dispose();
        _blocksWhoisCts = null;
    }

    private void CancelAttemptWhois()
    {
        _attemptsWhoisCts?.Cancel();
        _attemptsWhoisCts?.Dispose();
        _attemptsWhoisCts = null;
    }

    private void CancelSuccessWhois()
    {
        _successWhoisCts?.Cancel();
        _successWhoisCts?.Dispose();
        _successWhoisCts = null;
    }
}
