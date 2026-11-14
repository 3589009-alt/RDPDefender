using System.Drawing.Drawing2D;

namespace RdpDefender.Ui;

/// <summary>Карточка со значением и подписью для верхней панели статистики.</summary>
public sealed class StatCard : Control
{
    private string _caption = "";
    private string _value = "—";

    public string Caption
    {
        get => _caption;
        set { _caption = value; Invalidate(); }
    }

    public string Value
    {
        get => _value;
        set { _value = value; Invalidate(); }
    }

    public Color Accent { get; set; } = Theme.Green;

    public StatCard()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Height = 92;
        Margin = new Padding(0, 0, 14, 0);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using var path = Theme.RoundedRect(bounds, 10f);

        using (var fill = new LinearGradientBrush(bounds, Color.FromArgb(16, 24, 18), Color.FromArgb(10, 14, 11), 90f))
            g.FillPath(fill, path);

        using (var pen = new Pen(Theme.Border, 1f))
            g.DrawPath(pen, path);

        using (var accent = new SolidBrush(Accent))
            g.FillRectangle(accent, 0, 20, 3, Height - 40);

        TextRenderer.DrawText(
            g, _caption.ToUpperInvariant(), Theme.Font(8.25f, FontStyle.Bold),
            new Rectangle(20, 15, Width - 30, 18), Theme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);

        TextRenderer.DrawText(
            g, _value, Theme.Font(20f, FontStyle.Bold),
            new Rectangle(18, 38, Width - 28, Height - 44), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);
    }
}
