using System.Drawing.Drawing2D;

namespace RdpDefender.Ui;

/// <summary>Переключатель из нескольких сегментов (способ блокировки).</summary>
public sealed class SegmentedControl : Control
{
    private string[] _options = Array.Empty<string>();
    private int _selectedIndex;
    private int _hoverIndex = -1;

    public event EventHandler? SelectedIndexChanged;

    public string[] Options
    {
        get => _options;
        set
        {
            _options = value ?? Array.Empty<string>();
            _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _options.Length - 1));
            Invalidate();
        }
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var index = Math.Clamp(value, 0, Math.Max(0, _options.Length - 1));
            if (index == _selectedIndex) return;
            _selectedIndex = index;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public SegmentedControl()
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
        Height = 40;
        Font = Theme.Font(9.25f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    private int HitTest(Point point)
    {
        if (_options.Length == 0 || Width <= 0) return -1;
        var index = point.X * _options.Length / Math.Max(1, Width);
        return Math.Clamp(index, 0, _options.Length - 1);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var index = HitTest(e.Location);
        if (index != _hoverIndex)
        {
            _hoverIndex = index;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverIndex = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            Focus();
            var index = HitTest(e.Location);
            if (index >= 0) SelectedIndex = index;
        }
        base.OnMouseDown(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Left or Keys.Up)
        {
            SelectedIndex = Math.Max(0, _selectedIndex - 1);
            e.Handled = true;
        }
        else if (e.KeyCode is Keys.Right or Keys.Down)
        {
            SelectedIndex = Math.Min(_options.Length - 1, _selectedIndex + 1);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using var track = Theme.RoundedRect(bounds, 9f);
        using (var fill = new SolidBrush(Theme.SurfaceAlt))
            g.FillPath(fill, track);
        using (var pen = new Pen(Theme.Border, 1f))
            g.DrawPath(pen, track);

        if (_options.Length == 0) return;

        var segmentWidth = Width / (float)_options.Length;

        for (var i = 0; i < _options.Length; i++)
        {
            var segment = new RectangleF(i * segmentWidth + 3, 3, segmentWidth - 6, Height - 6);
            using var segmentPath = Theme.RoundedRect(segment, 7f);

            if (i == _selectedIndex)
            {
                using var selectedFill = new SolidBrush(Theme.GreenDim);
                g.FillPath(selectedFill, segmentPath);
                using var selectedBorder = new Pen(Theme.GreenDark, 1f);
                g.DrawPath(selectedBorder, segmentPath);
            }
            else if (i == _hoverIndex)
            {
                using var hoverFill = new SolidBrush(Theme.SurfaceHover);
                g.FillPath(hoverFill, segmentPath);
            }

            var textColor = i == _selectedIndex ? Theme.Green : Theme.TextMuted;
            TextRenderer.DrawText(
                g,
                _options[i],
                Font,
                Rectangle.Round(segment),
                textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        if (Focused)
        {
            using var focus = new Pen(Color.FromArgb(90, Theme.Green));
            g.DrawPath(focus, track);
        }
    }
}
