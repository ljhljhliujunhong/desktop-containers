using System.Windows;
using System.Windows.Interop;

namespace DesktopContainers;

public static class MonitorGuard
{
    public static void ClampIfOffscreen(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var rect)) return;
        if (VisibleArea(rect) >= 80 * 80) return;

        var screen = Nearest(rect);
        if (screen == null) return;
        var work = screen.WorkingArea;
        var width = Math.Max(80, rect.Right - rect.Left);
        var height = Math.Max(80, rect.Bottom - rect.Top);
        var x = rect.Left;
        var y = rect.Top;
        if (x + 80 > work.Right) x = work.Right - Math.Min(width, work.Width);
        if (y + 80 > work.Bottom) y = work.Bottom - Math.Min(height, work.Height);
        if (x < work.Left) x = work.Left;
        if (y < work.Top) y = work.Top;
        NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            x,
            y,
            0,
            0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    public static void CenterOnCursor(Window window, int stagger)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !NativeMethods.GetCursorPos(out var point)) return;
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(point.X, point.Y));
        var work = screen.WorkingArea;
        if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return;
        var width = Math.Max(100, rect.Right - rect.Left);
        var height = Math.Max(100, rect.Bottom - rect.Top);
        var x = point.X - width / 2 + stagger * 16;
        var y = point.Y - height / 2 + stagger * 16;
        if (x + width > work.Right) x = Math.Max(work.Left, work.Right - width);
        if (y + height > work.Bottom) y = Math.Max(work.Top, work.Bottom - height);
        NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            x,
            y,
            0,
            0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    public static void NudgeApart(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var rect)) return;
        var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
        var work = screen.WorkingArea;
        var width = Math.Max(100, rect.Right - rect.Left);
        var height = Math.Max(100, rect.Bottom - rect.Top);
        var x = rect.Left;
        var y = rect.Top;
        for (var attempt = 0; attempt < 16 && CoversAnother(hwnd, x, y, width, height); attempt++)
        {
            x += 72;
            y += 56;
            if (x + 120 > work.Right || y + 120 > work.Bottom)
            {
                x = work.Left + 24;
                y = work.Top + 24 + (attempt % 4) * 40;
            }
        }
        if (x + width > work.Right) x = Math.Max(work.Left, work.Right - width);
        if (y + height > work.Bottom) y = Math.Max(work.Top, work.Bottom - height);
        if (x < work.Left) x = work.Left;
        if (y < work.Top) y = work.Top;
        if (x == rect.Left && y == rect.Top) return;
        NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            x,
            y,
            0,
            0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    static bool CoversAnother(IntPtr hwnd, int x, int y, int width, int height)
    {
        var mine = new NativeMethods.RECT { Left = x, Top = y, Right = x + width, Bottom = y + height };
        foreach (var other in AppHost.Windows)
        {
            var otherHwnd = new WindowInteropHelper(other).Handle;
            if (otherHwnd == IntPtr.Zero || otherHwnd == hwnd) continue;
            if (!NativeMethods.GetWindowRect(otherHwnd, out var rect)) continue;
            var left = Math.Max(mine.Left, rect.Left);
            var top = Math.Max(mine.Top, rect.Top);
            var right = Math.Min(mine.Right, rect.Right);
            var bottom = Math.Min(mine.Bottom, rect.Bottom);
            if (right - left > 160 && bottom - top > 120) return true;
        }
        return false;
    }

    public static string? DeviceName(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return null;
        return System.Windows.Forms.Screen.FromHandle(hwnd).DeviceName;
    }

    static int VisibleArea(NativeMethods.RECT rect)
    {
        var area = 0;
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var work = screen.WorkingArea;
            var left = Math.Max(rect.Left, work.Left);
            var top = Math.Max(rect.Top, work.Top);
            var right = Math.Min(rect.Right, work.Right);
            var bottom = Math.Min(rect.Bottom, work.Bottom);
            if (right > left && bottom > top)
                area += (right - left) * (bottom - top);
        }
        return area;
    }

    static System.Windows.Forms.Screen? Nearest(NativeMethods.RECT rect)
    {
        System.Windows.Forms.Screen? best = null;
        var score = long.MaxValue;
        var cx = (rect.Left + rect.Right) / 2;
        var cy = (rect.Top + rect.Bottom) / 2;
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var work = screen.WorkingArea;
            var dx = cx - (work.Left + work.Width / 2);
            var dy = cy - (work.Top + work.Height / 2);
            var dist = (long)dx * dx + (long)dy * dy;
            if (dist < score)
            {
                score = dist;
                best = screen;
            }
        }
        return best;
    }
}
