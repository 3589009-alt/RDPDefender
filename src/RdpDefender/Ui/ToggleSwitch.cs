using System.Drawing.Drawing2D;

namespace RdpDefender.Ui;

/// <summary>Переключатель с подписью и пояснением — вместо стандартного CheckBox.</summary>
public sealed class ToggleSwitch : Control
{
    private bool _checked;
    private string _description = "";
    private bool _hover;

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string Description
    {
        get => _description;
        set { _description = value ?? ""; Invalidate(); }
    }

    public ToggleSwitch()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.Selectable,
            true);
        BackColor = Color.Transparent;
        Height = 46;
        Font = Theme.Font(9.5f);
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        Invalidate();
        base.OnTextChanged(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && Enabled)
        {
            Focus();
            Checked = !Checked;
        }
        base.OnMouseDown(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Enabled && e.KeyCode is Keys.Space or Keys.Enter)
        {
            Checked = !Checked;
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var titleColor = Enabled ? (_hover ? Theme.Text : Color.FromArgb(214, 232, 220)) : Theme.TextDim;
        var mutedColor = Enabled ? Theme.TextMuted : Theme.TextDim;

        var trackWidth = 44f;
        var trackHeight = 24f;
        var trackX = Width - trackWidth - 2;
        var trackY = (Height - trackHeight) / 2f;

        var titleHeight = string.IsNullOrWhiteSpace(_description) ? 20 : 18;
        var titleY = string.IsNullOrWhiteSpace(_description) ? (Height - titleHeight) / 2 : 4;

        TextRenderer.DrawText(
            g, Text, Font,
            new Rectangle(0, titleY, Math.Max(10, (int)trackX - 14), titleHeight),
            titleColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);

        if (!string.IsNullOrWhiteSpace(_description))
        {
            TextRenderer.DrawText(
                g, _description, Theme.Font(8.25f),
                new Rectangle(0, titleY + 20, Math.Max(10, (int)trackX - 14), 16),
                mutedColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }

        var trackRect = new RectangleF(trackX, trackY, trackWidth, trackHeight);
        using var trackPath = Theme.RoundedRect(trackRect, trackHeight / 2f);

        var trackFill = !Enabled
            ? Color.FromArgb(26, 36, 29)
            : _checked
                ? (_hover ? Theme.GreenBright : Theme.Green)
                : Color.FromArgb(34, 48, 39);
        using (var brush = new SolidBrush(trackFill))
            g.FillPath(brush, trackPath);

        using (var pen = new Pen(_checked ? Theme.Green : Color.FromArgb(48, 64, 52), 1f))
            g.DrawPath(pen, trackPath);

        var knobSize = 18f;
        var knobX = _checked ? trackX + trackWidth - knobSize - 3 : trackX + 3;
        var knobY = trackY + (trackHeight - knobSize) / 2f;
        using (var knob = new SolidBrush(!Enabled ? Color.FromArgb(96, 112, 101) : _checked ? Color.FromArgb(5, 22, 12) : Color.FromArgb(170, 190, 177)))
            g.FillEllipse(knob, knobX, knobY, knobSize, knobSize);

        if (Focused)
        {
            using var focus = new Pen(Color.FromArgb(80, Theme.Green)) { DashStyle = DashStyle.Dot };
            g.DrawRectangle(focus, 0, 0, Width - 1, Height - 1);
        }
    }
}
