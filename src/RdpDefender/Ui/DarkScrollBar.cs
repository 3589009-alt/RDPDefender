using System.Drawing.Drawing2D;

namespace RdpDefender.Ui;

/// <summary>Тонкий тёмный вертикальный скроллбар.</summary>
public sealed class DarkScrollBar : Control
{
    private int _maximum;
    private int _viewport;
    private int _value;
    private bool _dragging;
    private int _dragOffset;
    private bool _hover;

    public event EventHandler? ValueChanged;

    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(0, value);
            Value = _value;
            Invalidate();
        }
    }

    public int Viewport
    {
        get => _viewport;
        set { _viewport = Math.Max(1, value); Invalidate(); }
    }

    public int Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, 0, _maximum);
            if (clamped == _value) return;
            _value = clamped;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public DarkScrollBar()
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
        Width = 10;
        TabStop = true;
    }

    private RectangleF TrackRect => new(2, 4, Width - 4, Math.Max(1, Height - 8));

    private float ThumbHeight
    {
        get
        {
            var content = _maximum + _viewport;
            if (content <= 0) return TrackRect.Height;
            return Math.Max(28f, TrackRect.Height * (_viewport / (float)content));
        }
    }

    private float ThumbTop
    {
        get
        {
            if (_maximum <= 0) return TrackRect.Y;
            var travel = TrackRect.Height - ThumbHeight;
            return TrackRect.Y + travel * (_value / (float)_maximum);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var track = new SolidBrush(Color.FromArgb(95, 24, 35, 28)))
        {
            using var trackPath = Theme.RoundedRect(TrackRect, 3f);
            g.FillPath(track, trackPath);
        }

        var thumbRect = new RectangleF(TrackRect.X, ThumbTop, TrackRect.Width, ThumbHeight);
        using var thumbPath = Theme.RoundedRect(thumbRect, 3f);
        using var thumbBrush = new SolidBrush(_dragging ? Theme.Green : _hover ? Theme.GreenDark : Color.FromArgb(200, 44, 110, 76));
        g.FillPath(thumbBrush, thumbPath);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        Focus();
        var thumbTop = ThumbTop;
        var thumbHeight = ThumbHeight;

        if (e.Y >= thumbTop && e.Y <= thumbTop + thumbHeight)
        {
            _dragging = true;
            _dragOffset = (int)(e.Y - thumbTop);
        }
        else
        {
            var direction = e.Y < thumbTop ? -1 : 1;
            Value += direction * Math.Max(40, _viewport * 9 / 10);
        }

        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var overThumb = e.Y >= ThumbTop && e.Y <= ThumbTop + ThumbHeight;
        if (overThumb != _hover)
        {
            _hover = overThumb;
            Cursor = overThumb ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        if (_dragging)
        {
            var travel = Math.Max(1f, TrackRect.Height - ThumbHeight);
            var relative = Math.Clamp(e.Y - TrackRect.Y - _dragOffset, 0, travel);
            Value = (int)Math.Round(relative / travel * _maximum);
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (!_dragging)
        {
            _hover = false;
            Invalidate();
        }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        Value -= Math.Sign(e.Delta) * 80;
        base.OnMouseWheel(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Up:
                Value -= 40;
                e.Handled = true;
                break;
            case Keys.Down:
                Value += 40;
                e.Handled = true;
                break;
            case Keys.PageUp:
                Value -= _viewport;
                e.Handled = true;
                break;
            case Keys.PageDown:
                Value += _viewport;
                e.Handled = true;
                break;
        }
        base.OnKeyDown(e);
    }
}
