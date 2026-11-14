using System.Drawing.Drawing2D;

namespace RdpDefender.Ui;

/// <summary>Числовое поле со стрелками в стиле темы.</summary>
public sealed class NumberInput : Control
{
    private readonly TextBox _box;
    private int _value = 1;
    private int _minimum = 1;
    private int _maximum = 1_000_000;
    private int _hoverPart;
    private bool _suppress;

    public event EventHandler? ValueChanged;

    public NumberInput()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);

        BackColor = Color.Transparent;
        Height = 36;
        Width = 150;

        _box = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.SurfaceAlt,
            ForeColor = Theme.Text,
            Font = Theme.Font(9.5f),
            TextAlign = HorizontalAlignment.Left,
            Text = "1"
        };

        _box.KeyDown += OnBoxKeyDown;
        _box.LostFocus += (_, _) => CommitText();
        _box.Enter += (_, _) => Invalidate();
        _box.Leave += (_, _) => Invalidate();
        Controls.Add(_box);
    }

    public int Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, _minimum, _maximum);
            if (_value == clamped && _box.Text == clamped.ToString())
                return;

            _value = clamped;
            _suppress = true;
            _box.Text = clamped.ToString();
            _suppress = false;
            Invalidate();
        }
    }

    public int Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            if (_maximum < _minimum) _maximum = _minimum;
            Value = Math.Clamp(_value, _minimum, _maximum);
            Invalidate();
        }
    }

    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(value, _minimum);
            Value = Math.Clamp(_value, _minimum, _maximum);
            Invalidate();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutBox();
        Invalidate();
    }

    private void LayoutBox()
    {
        if (_box is null) return;
        var height = Math.Max(16, Height - 18);
        _box.SetBounds(12, (Height - height) / 2, Math.Max(10, Width - 48), height);
    }

    private Rectangle UpRect => new(Width - 28, 2, 26, Height / 2 - 2);
    private Rectangle DownRect => new(Width - 28, Height / 2, 26, Height / 2 - 2);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var part = UpRect.Contains(e.Location) ? 1 : DownRect.Contains(e.Location) ? -1 : 0;
        if (part != _hoverPart)
        {
            _hoverPart = part;
            Cursor = part == 0 ? Cursors.Default : Cursors.Hand;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverPart = 0;
        Cursor = Cursors.Default;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            if (UpRect.Contains(e.Location)) { Step(1); return; }
            if (DownRect.Contains(e.Location)) { Step(-1); return; }
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (Focused)
        {
            Step(e.Delta > 0 ? 1 : -1);
        }
        base.OnMouseWheel(e);
    }

    private void OnBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Up) { Step(1); e.Handled = true; e.SuppressKeyPress = true; }
        else if (e.KeyCode == Keys.Down) { Step(-1); e.Handled = true; e.SuppressKeyPress = true; }
        else if (e.KeyCode == Keys.Enter) { CommitText(); e.Handled = true; e.SuppressKeyPress = true; }
    }

    private void Step(int direction)
    {
        var delta = direction * (ModifierKeys.HasFlag(Keys.Shift) ? 10 : 1);
        var next = Math.Clamp(_value + delta, _minimum, _maximum);
        if (next == _value) return;
        _value = next;
        _suppress = true;
        _box.Text = next.ToString();
        _suppress = false;
        Invalidate();
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CommitText()
    {
        if (_suppress) return;
        if (!int.TryParse(_box.Text.Trim(), out var parsed))
        {
            Value = _value;
            return;
        }

        var clamped = Math.Clamp(parsed, _minimum, _maximum);
        var changed = clamped != _value;
        _value = clamped;
        _suppress = true;
        _box.Text = clamped.ToString();
        _suppress = false;
        Invalidate();

        if (changed)
            ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using var path = Theme.RoundedRect(bounds, 8f);
        using (var fill = new SolidBrush(Theme.SurfaceAlt))
            g.FillPath(fill, path);
        using (var pen = new Pen(_box.Focused ? Theme.GreenDark : Theme.Border, 1f))
            g.DrawPath(pen, path);

        using (var separator = new Pen(Theme.BorderSoft, 1f))
            g.DrawLine(separator, Width - 28, 7, Width - 28, Height - 7);

        DrawArrow(g, UpRect, true, _hoverPart == 1);
        DrawArrow(g, DownRect, false, _hoverPart == -1);
    }

    private static void DrawArrow(Graphics g, Rectangle area, bool up, bool hover)
    {
        var color = hover ? Theme.Green : Theme.TextMuted;
        var cx = area.X + area.Width / 2f;
        var cy = area.Y + area.Height / 2f;
        var size = 4f;

        var points = up
            ? new[] { new PointF(cx - size, cy + size / 2), new PointF(cx + size, cy + size / 2), new PointF(cx, cy - size / 2) }
            : new[] { new PointF(cx - size, cy - size / 2), new PointF(cx + size, cy - size / 2), new PointF(cx, cy + size / 2) };

        using var brush = new SolidBrush(color);
        g.FillPolygon(brush, points);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        _box.ReadOnly = !Enabled;
        _box.ForeColor = Enabled ? Theme.Text : Theme.TextDim;
        Invalidate();
        base.OnEnabledChanged(e);
    }
}
