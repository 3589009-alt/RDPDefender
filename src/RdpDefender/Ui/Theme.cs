using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace RdpDefender.Ui;

/// <summary>Палитра и графические помощники тёмно-зелёной темы.</summary>
public static class Theme
{
    public static readonly Color Bg = Color.FromArgb(7, 10, 8);
    public static readonly Color HeaderTop = Color.FromArgb(6, 9, 7);
    public static readonly Color HeaderBottom = Color.FromArgb(12, 26, 18);
    public static readonly Color Surface = Color.FromArgb(13, 18, 14);
    public static readonly Color SurfaceAlt = Color.FromArgb(17, 24, 19);
    public static readonly Color SurfaceHover = Color.FromArgb(22, 33, 26);
    public static readonly Color Border = Color.FromArgb(30, 43, 34);
    public static readonly Color BorderSoft = Color.FromArgb(22, 32, 25);
    public static readonly Color Text = Color.FromArgb(230, 242, 234);
    public static readonly Color TextMuted = Color.FromArgb(143, 167, 154);
    public static readonly Color TextDim = Color.FromArgb(94, 116, 102);
    public static readonly Color Green = Color.FromArgb(53, 224, 138);
    public static readonly Color GreenBright = Color.FromArgb(94, 240, 163);
    public static readonly Color GreenDark = Color.FromArgb(31, 169, 104);
    public static readonly Color GreenDim = Color.FromArgb(20, 48, 33);
    public static readonly Color GreenGlow = Color.FromArgb(35, 70, 48);
    public static readonly Color Danger = Color.FromArgb(255, 107, 107);
    public static readonly Color DangerDark = Color.FromArgb(92, 38, 38);
    public static readonly Color Warn = Color.FromArgb(245, 184, 75);
    public static readonly Color AmberDim = Color.FromArgb(58, 45, 20);

    public static Font Font(float size, FontStyle style = FontStyle.Regular)
        => new("Segoe UI", size, style, GraphicsUnit.Point);

    public static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var r = Math.Max(0, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2f));
        var d = r * 2;
        if (d <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Color Blend(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            (int)(from.A + (to.A - from.A) * amount),
            (int)(from.R + (to.R - from.R) * amount),
            (int)(from.G + (to.G - from.G) * amount),
            (int)(from.B + (to.B - from.B) * amount));
    }

    public static void EnableDarkTitleBar(IntPtr handle)
    {
        try
        {
            var enabled = 1;
            if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
        }
        catch { }
    }

    public static void UseDarkScrollbars(Control control)
    {
        try
        {
            if (control.IsHandleCreated)
                SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
        }
        catch { }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? subAppName, string? subIdList);
}
