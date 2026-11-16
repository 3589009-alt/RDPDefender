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
}
