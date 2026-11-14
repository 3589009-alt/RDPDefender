using RdpDefender.Core;

namespace RdpDefender.Ui;

/// <summary>Диалог ручной блокировки IP-адреса.</summary>
public sealed class BlockIpDialog : Form
{
    private readonly DarkInput _ip;
    private readonly NumberInput _minutes;
    private readonly ToggleSwitch _permanent;
    private readonly Label _error;

    public string Ip { get; private set; } = "";
    public int? Minutes => _permanent.Checked ? null : _minutes.Value;

    public BlockIpDialog()
    {
        Text = "Блокировка IP-адреса";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        BackColor = Theme.Bg;
        ClientSize = new Size(480, 362);
        Font = Theme.Font(9.5f);
        DoubleBuffered = true;

        var title = new Label
        {
            Text = "Блокировка IP-адреса",
            Font = Theme.Font(15f, FontStyle.Bold),
            ForeColor = Theme.Text,
            Location = new Point(26, 20),
            AutoSize = true
        };

        var subtitle = new Label
        {
            Text = "Блокировка будет применена службой RDP Defender.",
            Font = Theme.Font(8.75f),
            ForeColor = Theme.TextMuted,
            Location = new Point(28, 52),
            AutoSize = true
        };

        var ipLabel = new Label
        {
            Text = "IP-адрес",
            Font = Theme.Font(9f),
            ForeColor = Theme.TextMuted,
            Location = new Point(28, 90),
            AutoSize = true
        };

        _ip = new DarkInput
        {
            Location = new Point(28, 112),
            Size = new Size(424, 36),
            PlaceholderText = "203.0.113.10"
        };

        var durationLabel = new Label
        {
            Text = "Длительность блокировки, минут",
            Font = Theme.Font(9f),
            ForeColor = Theme.TextMuted,
            Location = new Point(28, 164),
            AutoSize = true
        };

        _minutes = new NumberInput
        {
            Minimum = 1,
            Maximum = 525_600,
            Value = 1440,
            Location = new Point(28, 186),
            Size = new Size(168, 36)
        };

        _permanent = new ToggleSwitch
        {
            Text = "Бессрочно",
            Description = "До ручного снятия",
            Location = new Point(216, 176),
            Size = new Size(236, 46)
        };
        _permanent.CheckedChanged += (_, _) => _minutes.Enabled = !_permanent.Checked;

        _error = new Label
        {
            Text = "",
            Font = Theme.Font(8.5f),
            ForeColor = Theme.Danger,
            Location = new Point(28, 242),
            Size = new Size(424, 20),
            AutoSize = false
        };

        var cancel = new AccentButton
        {
            Text = "Отмена",
            Kind = AccentKind.Secondary,
            Size = new Size(112, 36),
            Location = new Point(222, 296),
            DialogResult = DialogResult.Cancel
        };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        var block = new AccentButton
        {
            Text = "Заблокировать",
            Kind = AccentKind.Primary,
            Size = new Size(142, 36),
            Location = new Point(344, 296)
        };
        block.Click += (_, _) => TryAccept();

        AcceptButton = block;
        CancelButton = cancel;

        Controls.Add(title);
        Controls.Add(subtitle);
        Controls.Add(ipLabel);
        Controls.Add(_ip);
        Controls.Add(durationLabel);
        Controls.Add(_minutes);
        Controls.Add(_permanent);
        Controls.Add(_error);
        Controls.Add(cancel);
        Controls.Add(block);

        _ip.ValueChanged += (_, _) => _error.Text = "";
        Shown += (_, _) => _ip.FocusInput();
    }

    private void TryAccept()
    {
        var text = _ip.Text.Trim();

        if (!IpMatcher.TryParseAddress(text, out var address) || address is null)
        {
            _error.Text = "Введите корректный IPv4- или IPv6-адрес.";
            _ip.FocusInput();
            return;
        }

        Ip = address.ToString();
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.EnableDarkTitleBar(Handle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var brush = new SolidBrush(Theme.Green);
        e.Graphics.FillRectangle(brush, 0, 22, 3, 24);
    }
}
