using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using RdpDefender.Core;
using RdpDefender.Infrastructure;

namespace RdpDefender.Ui;

/// <summary>Подробные сведения о выбранном IP: локальные данные, агрегаты и whois.</summary>
public sealed class AttackerDetailsPanel : Panel
{
    private readonly Label _title;
    private readonly Label _subtitle;
    private readonly RichTextBox _text;
    private readonly AccentButton _copy;
    private readonly AccentButton _refresh;
    private readonly Font _mono;
    private string _ip = "";
    private string _baseInfo = "";
    private string _whoisInfo = "";

    public event EventHandler? RefreshRequested;

    public string Ip => _ip;

    public AttackerDetailsPanel()
    {
        BackColor = Theme.Surface;
        Padding = new Padding(14, 12, 14, 12);
        Margin = new Padding(0, 10, 0, 0);
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        _mono = new Font("Consolas", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
        _title = new Label
        {
            Text = "Данные атакующего",
            AutoSize = true,
            ForeColor = Theme.Text,
            Font = Theme.Font(10f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 0)
        };
        _subtitle = new Label
        {
            Text = "Выберите строку в списке",
            AutoSize = true,
            ForeColor = Theme.TextDim,
            Font = Theme.Font(8.25f),
            Margin = new Padding(0, 2, 0, 0)
        };

        var titleStack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        titleStack.Controls.Add(_title);
        titleStack.Controls.Add(_subtitle);

        _copy = new AccentButton
        {
            Text = "Копировать",
            Kind = AccentKind.Ghost,
            Width = 118,
            Height = 28,
            Margin = new Padding(0, 0, 8, 0)
        };
        _copy.Click += (_, _) => CopyAll();

        _refresh = new AccentButton
        {
            Text = "Обновить WHOIS",
            Kind = AccentKind.Secondary,
            Width = 154,
            Height = 28,
            Margin = new Padding(0)
        };
        _refresh.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0),
            AutoSize = false
        };
        actions.Controls.Add(_copy);
        actions.Controls.Add(_refresh);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 48,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        header.Controls.Add(titleStack, 0, 0);
        header.Controls.Add(actions, 1, 0);

        _text = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(9, 14, 10),
            ForeColor = Theme.Text,
            Font = _mono,
            ReadOnly = true,
            WordWrap = false,
            DetectUrls = false,
            ScrollBars = RichTextBoxScrollBars.Both,
            ShortcutsEnabled = true,
            Margin = new Padding(0)
        };

        Controls.Add(_text);
        Controls.Add(header);
        Clear("Выберите строку в списке — здесь появятся все данные об атакующем.");
    }

    public void ShowBlock(BlockedIpView block, IReadOnlyList<FailedAttemptView> attempts)
    {
        _ip = block.Ip;
        _title.Text = $"IP {block.Ip}";
        _subtitle.Text = "Активная блокировка · локальные данные, агрегаты и WHOIS";

        var lines = new List<string>
        {
            Field("IP-адрес", block.Ip),
            Field("Состояние", block.Simulated ? "Наблюдение (правило не создаётся)" :
                block.Applied ? "Блокировка применена" : "Ожидает применения"),
            Field("Заблокирован", Local(block.BlockedAtUtc)),
            Field("Действует до", block.ExpiresAtUtc is null ? "Бессрочно" : Local(block.ExpiresAtUtc.Value)),
            Field("Причина", block.Reason),
            Field("Номер блокировки", block.BlockNumber.ToString(CultureInfo.InvariantCulture)),
            Field("Попыток до блокировки", block.Attempts.ToString(CultureInfo.InvariantCulture)),
            Field("Постоянная", block.Permanent ? "Да" : "Нет")
        };

        AppendAttemptAggregates(lines, attempts);
        _baseInfo = string.Join(Environment.NewLine, lines);
        _whoisInfo = "WHOIS: запрос выполняется…";
        Render();
    }

    public void ShowAttempt(FailedAttemptView attempt, IReadOnlyList<FailedAttemptView> attempts, BlockedIpView? block)
    {
        _ip = attempt.Ip;
        _title.Text = $"IP {attempt.Ip}";
        _subtitle.Text = "Неудачная попытка входа · локальные данные, агрегаты и WHOIS";

        var lines = new List<string>
        {
            Field("Время", Local(attempt.TimeUtc)),
            Field("IP-адрес", attempt.Ip),
            Field("Пользователь", Dash(attempt.UserName)),
            Field("Тип входа", LogonTypeText(attempt.LogonType)),
            Field("Рабочая станция", Dash(attempt.Workstation)),
            Field("Status", Dash(attempt.Status)),
            Field("SubStatus", Dash(attempt.SubStatus)),
            Field("Результат", Dash(attempt.Outcome))
        };

        if (block is not null)
        {
            lines.Add("");
            lines.Add(Field("Блокировка", block.Applied ? "Активна" : block.Simulated ? "Наблюдение" : "Ожидает"));
            lines.Add(Field("Причина блокировки", block.Reason));
            lines.Add(Field("Действует до", block.ExpiresAtUtc is null ? "Бессрочно" : Local(block.ExpiresAtUtc.Value)));
        }

        AppendAttemptAggregates(lines, attempts);
        _baseInfo = string.Join(Environment.NewLine, lines);
        _whoisInfo = "WHOIS: запрос выполняется…";
        Render();
    }

    public void ShowSuccess(
        SuccessfulLogonView success,
        IReadOnlyList<FailedAttemptView> attempts,
        BlockedIpView? block,
        bool protectionEnabled,
        bool whitelisted,
        bool blacklisted)
    {
        _ip = success.Ip;
        _title.Text = $"IP {success.Ip}";
        _subtitle.Text = "Успешное подключение · локальные данные, агрегаты и WHOIS";

        var state = blacklisted ? "Чёрный список"
            : block is not null ? "Заблокирован"
            : whitelisted ? "Белый список"
            : protectionEnabled ? "Защищён"
            : "Может блокироваться";

        var lines = new List<string>
        {
            Field("IP-адрес", success.Ip),
            Field("Первый успешный вход", Local(success.FirstSuccessUtc)),
            Field("Последний успешный вход", Local(success.LastSuccessUtc)),
            Field("Количество входов", success.Count.ToString(CultureInfo.InvariantCulture)),
            Field("Источник", Dash(success.Source)),
            Field("Пользователь", Dash(success.UserName)),
            Field("Рабочая станция", Dash(success.Workstation)),
            Field("Защита от блокировки", state)
        };

        if (block is not null)
        {
            lines.Add("");
            lines.Add(Field("Блокировка", block.Applied ? "Активна" : block.Simulated ? "Наблюдение" : "Ожидает"));
            lines.Add(Field("Причина блокировки", block.Reason));
            lines.Add(Field("Действует до", block.ExpiresAtUtc is null ? "Бессрочно" : Local(block.ExpiresAtUtc.Value)));
        }

        AppendAttemptAggregates(lines, attempts);
        _baseInfo = string.Join(Environment.NewLine, lines);
        _whoisInfo = "WHOIS: запрос выполняется…";
        Render();
    }

    public void SetWhoisLoading()
    {
        _whoisInfo = "WHOIS: запрос выполняется…";
        Render();
    }

    public void SetWhois(AttackerLookup lookup)
    {
        var lines = new List<string>
        {
            Field("PTR", Dash(lookup.Ptr)),
            Field("Страна", Dash(lookup.Country)),
            Field("Сеть", Dash(lookup.Network)),
            Field("Организация", Dash(lookup.Organization)),
            Field("ASN", Dash(lookup.Asn)),
            Field("Abuse", Dash(lookup.Abuse)),
            Field("WHOIS получен", Local(lookup.LookedUpUtc))
        };

        if (!string.IsNullOrWhiteSpace(lookup.Error))
            lines.Add(Field("Ошибка", lookup.Error));

        lines.Add("");
        lines.Add(string.IsNullOrWhiteSpace(lookup.RawWhois) ? "WHOIS: данных нет." : lookup.RawWhois);
        _whoisInfo = string.Join(Environment.NewLine, lines);
        Render();
    }

    public void SetWhoisError(string message)
    {
        _whoisInfo = "WHOIS: " + message;
        Render();
    }

    public void Clear(string message)
    {
        _ip = "";
        _title.Text = "Данные атакующего";
        _subtitle.Text = message;
        _baseInfo = "";
        _whoisInfo = "";
        _text.Text = "";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Theme.BorderSoft, 1f);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _mono.Dispose();
            _copy.Dispose();
            _refresh.Dispose();
        }
        base.Dispose(disposing);
    }

    private void Render()
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(_baseInfo))
            sb.AppendLine(_baseInfo);
        if (!string.IsNullOrWhiteSpace(_whoisInfo))
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(_whoisInfo);
        }

        _text.Text = sb.ToString();
        _text.SelectionStart = 0;
        _text.SelectionLength = 0;
    }

    private void CopyAll()
    {
        if (string.IsNullOrWhiteSpace(_text.Text)) return;
        try { Clipboard.SetText(_text.Text); } catch { }
    }

    private static void AppendAttemptAggregates(List<string> lines, IReadOnlyList<FailedAttemptView> attempts)
    {
        lines.Add("");
        lines.Add(Field("События 4625 в журнале", attempts.Count.ToString(CultureInfo.InvariantCulture)));

        if (attempts.Count == 0)
        {
            lines.Add("Подробных попыток по этому IP в текущем журнале нет.");
            return;
        }

        var ordered = attempts.OrderBy(a => a.TimeUtc).ToList();
        var users = ordered.Select(a => a.UserName).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().ToList();
        var stations = ordered.Select(a => a.Workstation).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().ToList();
        var types = ordered.Select(a => LogonTypeText(a.LogonType)).Distinct().ToList();
        var outcomes = ordered.Select(a => a.Outcome).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().ToList();
        var statuses = ordered
            .Where(a => !string.IsNullOrWhiteSpace(a.Status) || !string.IsNullOrWhiteSpace(a.SubStatus))
            .Select(a => $"{Dash(a.Status)} / {Dash(a.SubStatus)}")
            .Distinct()
            .ToList();

        lines.Add(Field("Первое событие", Local(ordered[0].TimeUtc)));
        lines.Add(Field("Последнее событие", Local(ordered[^1].TimeUtc)));
        lines.Add(Field("Пользователи", JoinOrDash(users)));
        lines.Add(Field("Рабочие станции", JoinOrDash(stations)));
        lines.Add(Field("Типы входа", JoinOrDash(types)));
        lines.Add(Field("Результаты", JoinOrDash(outcomes)));
        if (statuses.Count > 0)
            lines.Add(Field("Status / SubStatus", JoinOrDash(statuses)));
    }

    private static string Field(string label, string value) =>
        $"{label,-26}{value}";

    private static string Dash(string value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static string JoinOrDash(IReadOnlyCollection<string> values) =>
        values.Count == 0 ? "—" : string.Join(", ", values);

    private static string Local(DateTime utc) =>
        utc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);

    private static string LogonTypeText(int logonType) => logonType switch
    {
        0 => "RDP-подключение",
        2 => "2 · интерактивный",
        3 => "3 · сетевой",
        7 => "7 · разблокировка",
        10 => "10 · RDP",
        _ => logonType.ToString(CultureInfo.InvariantCulture)
    };
}
