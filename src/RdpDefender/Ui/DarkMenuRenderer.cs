using System.Drawing.Drawing2D;

namespace RdpDefender.Ui;

/// <summary>Тёмное контекстное меню для значка в трее.</summary>
public sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Theme.Surface);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Theme.Surface);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var item = e.Item;
        var strip = e.ToolStrip;
        if (strip is null) return;
        var bounds = new Rectangle(2, 0, strip.Width - 4, item.Height);
        using var path = Theme.RoundedRect(bounds, 6f);
        using var brush = new SolidBrush(item.Selected ? Theme.SurfaceHover : Theme.Surface);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Selected ? Theme.Text : Color.FromArgb(210, 228, 216);
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(Theme.BorderSoft);
        var y = e.Item.Height / 2;
        var strip = e.ToolStrip;
        if (strip is null) return;
        e.Graphics.DrawLine(pen, 8, y, strip.Width - 8, y);
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Surface;
        public override Color ImageMarginGradientBegin => Theme.Surface;
        public override Color ImageMarginGradientMiddle => Theme.Surface;
        public override Color ImageMarginGradientEnd => Theme.Surface;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Theme.GreenDim;
        public override Color MenuItemSelected => Theme.SurfaceHover;
        public override Color MenuItemSelectedGradientBegin => Theme.SurfaceHover;
        public override Color MenuItemSelectedGradientEnd => Theme.SurfaceHover;
        public override Color SeparatorDark => Theme.BorderSoft;
        public override Color SeparatorLight => Theme.BorderSoft;
        public override Color CheckBackground => Theme.GreenDim;
        public override Color CheckSelectedBackground => Theme.GreenDim;
        public override Color CheckPressedBackground => Theme.GreenDim;
        public override Color ButtonSelectedHighlight => Theme.SurfaceHover;
    }
}
