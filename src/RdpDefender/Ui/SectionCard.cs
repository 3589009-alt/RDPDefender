using System.Drawing.Drawing2D;

namespace RdpDefender.Ui;

/// <summary>Секция настроек с заголовком, подписью и сеткой содержимого.</summary>
public sealed class SectionCard : Panel
{
    private readonly string _title;
    private readonly string _subtitle;

    public TableLayoutPanel Content { get; }

    public SectionCard(string title, string subtitle = "")
    {
        _title = title;
        _subtitle = subtitle;

        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);

        BackColor = Color.Transparent;
        Dock = DockStyle.Top;
        Margin = new Padding(0, 0, 0, 14);
        Padding = new Padding(20, string.IsNullOrEmpty(subtitle) ? 52 : 62, 20, 18);
        Height = 140;

        Content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        Content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
        Content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(Content);
    }

    public void AddRow(string label, Control control, int height = 46)
    {
        var row = Content.RowCount;
        Content.RowCount = row + 1;
        Content.RowStyles.Add(new RowStyle(SizeType.Absolute, height));

        var caption = new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            ForeColor = Theme.Text,
            Font = Theme.Font(9.5f),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 24, 0),
            AutoSize = false
        };

        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, (height - Math.Min(control.Height, height)) / 2, 0,
            (height - Math.Min(control.Height, height)) / 2);

        Content.Controls.Add(caption, 0, row);
        Content.Controls.Add(control, 1, row);
    }

    public void AddFullRow(Control control, int height = 46)
    {
        var row = Content.RowCount;
        Content.RowCount = row + 1;
        Content.RowStyles.Add(new RowStyle(SizeType.Absolute, height));

        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0);
        Content.Controls.Add(control, 0, row);
        Content.SetColumnSpan(control, 2);
    }

    public void AddHint(string text, int height = 26)
    {
        var label = new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = Theme.TextDim,
            Font = Theme.Font(8.25f),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
            AutoSize = false
        };

        var row = Content.RowCount;
        Content.RowCount = row + 1;
        Content.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        Content.Controls.Add(label, 0, row);
        Content.SetColumnSpan(label, 2);
    }

    public void FitToContent()
    {
        var total = Padding.Top + Padding.Bottom;
        foreach (RowStyle style in Content.RowStyles)
        {
            if (style.SizeType == SizeType.Absolute)
                total += (int)style.Height;
        }
        Height = total;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using var path = Theme.RoundedRect(bounds, 10f);
        using (var fill = new SolidBrush(Theme.Surface))
            g.FillPath(fill, path);
        using (var pen = new Pen(Theme.Border, 1f))
            g.DrawPath(pen, path);

        using (var accent = new SolidBrush(Theme.Green))
            g.FillRectangle(accent, 0, 20, 3, 18);

        TextRenderer.DrawText(
            g, _title, Theme.Font(10.5f, FontStyle.Bold),
            new Rectangle(20, 14, Width - 40, 22), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        if (!string.IsNullOrEmpty(_subtitle))
        {
            TextRenderer.DrawText(
                g, _subtitle, Theme.Font(8.25f),
                new Rectangle(20, 36, Width - 40, 18), Theme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}
