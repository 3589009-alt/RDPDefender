using System.Runtime.InteropServices;

namespace RdpDefender.Ui;

/// <summary>
/// Направляет WM_MOUSEWHEEL контролу под курсором, а не контролу в фокусе.
/// </summary>
public sealed class MouseWheelRouter : IMessageFilter
{
    private const int WmMouseWheel = 0x020A;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != WmMouseWheel) return false;

        if (!GetCursorPos(out var point)) return false;
        var handle = WindowFromPoint(point);
        if (handle == IntPtr.Zero) return false;

        var hovered = Control.FromHandle(handle);
        var target = FindScrollTarget(hovered);
        if (target is null || !target.IsHandleCreated) return false;

        var original = Control.FromHandle(m.HWnd);
        if (ReferenceEquals(target, original)) return false;

        var lParam = (IntPtr)((point.Y << 16) | (point.X & 0xFFFF));
        SendMessage(target.Handle, WmMouseWheel, m.WParam, lParam);
        return true;
    }

    private static Control? FindScrollTarget(Control? control)
    {
        for (var current = control; current is not null; current = current.Parent)
        {
            if (CanScroll(current)) return current;
        }
        return null;
    }

    private static bool CanScroll(Control control) => control switch
    {
        DarkScrollPanel => true,
        DataGridView => true,
        ListBox => true,
        ListView => true,
        TreeView => true,
        DarkScrollBar => true,
        RichTextBox rich => rich.ScrollBars != RichTextBoxScrollBars.None,
        TextBox text => text.Multiline && text.ScrollBars != ScrollBars.None,
        ScrollableControl scrollable => scrollable.AutoScroll,
        _ => false
    };
}
