using System.Drawing.Drawing2D;

namespace RdpDefender.Ui;

/// <summary>Поле ввода с рамкой и тёмным оформлением (однострочное и многострочное).</summary>
public sealed class DarkInput : UserControl
{
    private readonly TextBox _box;
    private readonly bool _multiline;

    public event EventHandler? ValueChanged;

    public DarkInput(bool multiline = false)
    {
        _multiline = multiline;
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);

        BackColor = Color.Transparent;
        Height = multiline ? 110 : 36;
        Padding = multiline ? new Padding(12, 10, 10, 10) : new Padding(12, 8, 12, 8);

        _box = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.SurfaceAlt,
            ForeColor = Theme.Text,
            Font = Theme.Font(9.5f),
            Dock = DockStyle.Fill,
            Multiline = multiline,
            ScrollBars = ScrollBars.None,
            AcceptsReturn = multiline,
            WordWrap = multiline
        };

        _box.TextChanged += (_, _) => ValueChanged?.Invoke(this, EventArgs.Empty);
        _box.Enter += (_, _) => Invalidate();
        _box.Leave += (_, _) => Invalidate();
        _box.HandleCreated += (_, _) =>
        {
            if (multiline) Theme.UseDarkScrollbars(_box);
        };

        Controls.Add(_box);
    }

    public new string Text
    {
        get => _box.Text;
        set => _box.Text = value;
    }

    public string PlaceholderText
    {
        get => _box.PlaceholderText;
        set => _box.PlaceholderText = value;
    }

    public bool ReadOnly
    {
        get => _box.ReadOnly;
        set
        {
            _box.ReadOnly = value;
            _box.BackColor = value ? Theme.Surface : Theme.SurfaceAlt;
        }
    }

    public TextBox Inner => _box;

    public void FocusInput() => _box.Focus();

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
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }
}
