using System.Drawing.Drawing2D;

namespace RdpDefender.Ui;

public enum AccentKind
{
    Primary,
    Secondary,
    Ghost,
    Danger
}

/// <summary>Кнопка с плоским скруглённым оформлением в стиле темы.</summary>
public sealed class AccentButton : Button
{
    private bool _hover;
    private bool _pressed;

    public AccentKind Kind { get; set; } = AccentKind.Secondary;
    public int CornerRadius { get; set; } = 8;

    public AccentButton()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);

        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        UseVisualStyleBackColor = false;
        Font = Theme.Font(9.25f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        Height = 34;
        UseMnemonic = false;
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
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        if (mevent.Button == MouseButtons.Left) _pressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        // Прозрачные кнопки при первичной раскладке могут оставлять след
        // на родительской панели — просим её перерисоваться.
        Parent?.Invalidate();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Parent?.Invalidate();
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // Заливаем всю область цветом родителя: это убирает следы
        // прозрачности при перерисовке и первичной раскладке.
        var background = Parent?.BackColor ?? Theme.Bg;
        if (background.A < 255) background = Theme.Bg;
        using (var backgroundBrush = new SolidBrush(background))
            g.FillRectangle(backgroundBrush, ClientRectangle);

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using var path = Theme.RoundedRect(bounds, CornerRadius);

        Color fill;
        Color border;
        Color text;

        if (!Enabled)
        {
            fill = Color.FromArgb(15, 21, 17);
            border = Color.FromArgb(26, 36, 29);
            text = Color.FromArgb(76, 96, 83);
        }
        else
        {
            switch (Kind)
            {
                case AccentKind.Primary:
                    fill = _pressed ? Theme.GreenDark : _hover ? Theme.GreenBright : Theme.Green;
                    border = fill;
                    text = Color.FromArgb(4, 18, 10);
                    break;
                case AccentKind.Danger:
                    fill = _pressed ? Color.FromArgb(58, 20, 20) : _hover ? Color.FromArgb(42, 18, 18) : Color.Transparent;
                    border = _hover ? Color.FromArgb(120, 52, 52) : Theme.DangerDark;
                    text = Theme.Danger;
                    break;
                case AccentKind.Ghost:
                    fill = _pressed ? Theme.SurfaceHover : _hover ? Color.FromArgb(19, 27, 21) : Color.Transparent;
                    border = Color.Transparent;
                    text = _hover ? Theme.Text : Theme.TextMuted;
                    break;
                default:
                    fill = _pressed ? Color.FromArgb(22, 44, 31) : _hover ? Theme.SurfaceHover : Theme.SurfaceAlt;
                    border = _hover ? Color.FromArgb(54, 84, 65) : Theme.Border;
                    text = Theme.Text;
                    break;
            }
        }

        if (fill.A > 0)
            using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);

        if (border.A > 0)
            using (var pen = new Pen(border, 1f)) g.DrawPath(pen, path);

        var textRect = new Rectangle(0, 0, Width, Height);
        TextRenderer.DrawText(
            g, Text, Font, textRect, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        if (Focused && Enabled)
        {
            using var focusPen = new Pen(Color.FromArgb(90, Theme.Green), 1f);
            g.DrawPath(focusPen, path);
        }
    }
}
