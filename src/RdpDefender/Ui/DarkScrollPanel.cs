namespace RdpDefender.Ui;

/// <summary>
/// Контейнер с собственной тёмной полосой прокрутки: содержимое
/// прокручивается колесом или перетаскиванием ползунка.
/// </summary>
public sealed class DarkScrollPanel : Panel
{
    private readonly Panel _viewport;
    private readonly Panel _canvas;
    private readonly Control _content;
    private readonly DarkScrollBar _bar;
    private int _offset;

    public DarkScrollPanel(Control content)
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);

        BackColor = Color.Transparent;
        Padding = new Padding(0);
        Margin = new Padding(0);

        _content = content;
        _bar = new DarkScrollBar { Dock = DockStyle.Right, Width = 12, Margin = new Padding(0) };
        _viewport = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = new Padding(0) };
        _canvas = new Panel { BackColor = Color.Transparent, Location = new Point(0, 0) };

        _content.Dock = DockStyle.Top;
        _content.AutoSize = true;

        _canvas.Controls.Add(_content);
        _viewport.Controls.Add(_canvas);

        Controls.Add(_viewport);
        Controls.Add(_bar);

        _bar.ValueChanged += (_, _) =>
        {
            _offset = _bar.Value;
            _canvas.Top = -_offset;
        };

        _viewport.Resize += (_, _) => Recalculate();
        _content.SizeChanged += (_, _) => Recalculate();
        _bar.Visible = false;
    }

    public Control Content => _content;

    public void ScrollToTop() => ScrollTo(0);

    public void ScrollTo(int offset)
    {
        _bar.Value = Math.Clamp(offset, 0, _bar.Maximum);
        _canvas.Top = -_bar.Value;
    }

    public void Recalculate()
    {
        var width = Math.Max(120, _viewport.ClientSize.Width);
        if (_canvas.Width != width)
            _canvas.Width = width;

        _content.PerformLayout();
        var contentHeight = Math.Max(0, _content.Height);
        if (_canvas.Height != contentHeight)
            _canvas.Height = contentHeight;

        var viewportHeight = Math.Max(1, _viewport.ClientSize.Height);
        var maximum = Math.Max(0, contentHeight - viewportHeight);

        _bar.Maximum = maximum;
        _bar.Viewport = viewportHeight;
        _bar.Visible = maximum > 0;
        if (!_bar.Visible)
        {
            _bar.Value = 0;
            _canvas.Top = 0;
        }

        if (_bar.Value > maximum)
            _bar.Value = maximum;

        _canvas.Top = -_bar.Value;
    }

    public void EnableMouseWheel()
    {
        AttachWheel(this);
    }

    private void AttachWheel(Control control)
    {
        control.MouseWheel += OnWheel;

        foreach (Control child in control.Controls)
        {
            if (child is TextBoxBase) continue;
            AttachWheel(child);
        }
    }

    private void OnWheel(object? sender, MouseEventArgs e)
    {
        if (!_bar.Visible) return;

        _bar.Value -= Math.Sign(e.Delta) * 80;

        if (e is HandledMouseEventArgs handled)
            handled.Handled = true;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Recalculate();
    }
}
