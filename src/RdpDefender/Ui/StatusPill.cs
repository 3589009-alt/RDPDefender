using System.Drawing.Drawing2D;

namespace RdpDefender.Ui;

/// <summary>Индикатор состояния в шапке: точка + подпись.</summary>
public sealed class StatusPill : Control
{
    private string _stateText = "НЕТ СВЯЗИ";
    private Color _accent = Theme.TextDim;

    public StatusPill()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Height = 34;
        Width = 210;
        Font = Theme.Font(8.25f, FontStyle.Bold);
    }

    public void SetState(string text, Color accent)
    {
        _stateText = text.ToUpperInvariant();
        _accent = accent;
        var width = TextRenderer.MeasureText(_stateText, Font).Width + 50;
        Width = Math.Max(150, width);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using var path = Theme.RoundedRect(bounds, Height / 2f);
        using (var fill = new SolidBrush(Color.FromArgb(26, _accent)))
            g.FillPath(fill, path);
        using (var pen = new Pen(Color.FromArgb(85, _accent), 1f))
            g.DrawPath(pen, path);

        using (var dot = new SolidBrush(_accent))
            g.FillEllipse(dot, 14, Height / 2f - 3.5f, 7, 7);

        TextRenderer.DrawText(
            g, _stateText, Font,
            new Rectangle(29, 0, Width - 36, Height), _accent,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}
