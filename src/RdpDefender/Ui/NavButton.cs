using System.Drawing.Drawing2D;

namespace RdpDefender.Ui;

/// <summary>Вкладка-кнопка в стиле тёмного интерфейса.</summary>
public sealed class NavButton : Control
{
    private bool _active;
    private bool _hover;

    public bool Active
    {
        get => _active;
        set { _active = value; Invalidate(); }
    }

    public NavButton()
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
        Height = 38;
        Width = 118;
        Font = Theme.Font(10f, FontStyle.Regular);
        Cursor = Cursors.Hand;
        TabStop = true;
        Margin = new Padding(0, 0, 6, 0);
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_hover && !_active)
        {
            using var hoverPath = Theme.RoundedRect(new RectangleF(0, 2, Width, Height - 4), 7f);
            using var hoverBrush = new SolidBrush(Color.FromArgb(16, 24, 18));
            g.FillPath(hoverBrush, hoverPath);
        }

        var color = _active ? Theme.Green : _hover ? Theme.Text : Theme.TextMuted;
        var textRect = new Rectangle(12, 0, Width - 20, Height - 4);
        TextRenderer.DrawText(
            g, Text, Font, textRect, color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        if (_active)
        {
            var textWidth = TextRenderer.MeasureText(Text, Font).Width;
            using var underline = new SolidBrush(Theme.Green);
            g.FillRectangle(underline, 12, Height - 3, Math.Min(textWidth, Width - 20), 2);
        }

        if (Focused)
        {
            using var pen = new Pen(Color.FromArgb(70, Theme.Green));
            g.DrawRectangle(pen, 0, 1, Width - 1, Height - 3);
        }
    }
}
