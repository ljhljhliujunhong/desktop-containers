using System.Windows;
using System.Windows.Threading;

namespace DesktopContainers;

/// <summary>
/// 容器是普通顶层分层窗口，不是 Progman 的子窗口。
/// Windows 11 24H2 起 Progman 带 WS_EX_NOREDIRECTIONBITMAP，透明子窗口经常画不出来。
/// 这里把容器压到 Progman 上方、普通程序下方，点击时不激活，避免它盖住正在用的软件。
/// </summary>
public static class DesktopPlacement
{
    const uint EventSystemForeground = 0x0003;
    const uint WinEventOutOfContext = 0x0000;
    const uint WinEventSkipOwnProcess = 0x0002;

    static readonly NativeMethods.WinEventDelegate Callback = OnEvent;
    static readonly List<IntPtr> Hwnds = new();
    static IntPtr _hook;

    public static bool Suspend { get; set; }

    public static void Install()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = NativeMethods.SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            IntPtr.Zero,
            Callback,
            0,
            0,
            WinEventOutOfContext | WinEventSkipOwnProcess);
    }

    public static void Register(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        if (!Hwnds.Contains(hwnd)) Hwnds.Add(hwnd);
        PinAll();
    }

    public static void Unregister(IntPtr hwnd) => Hwnds.Remove(hwnd);

    public static void PinAll()
    {
        if (Suspend) return;
        var progman = NativeMethods.FindWindow("Progman", "Program Manager");
        if (progman == IntPtr.Zero) return;

        var anchor = NativeMethods.GetWindow(progman, NativeMethods.GW_HWNDPREV);
        while (anchor != IntPtr.Zero && (Hwnds.Contains(anchor) || !NativeMethods.IsWindowVisible(anchor)))
            anchor = NativeMethods.GetWindow(anchor, NativeMethods.GW_HWNDPREV);

        var flags = NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE;
        foreach (var hwnd in Hwnds.ToList())
        {
            if (!NativeMethods.IsWindow(hwnd)) continue;
            var after = anchor == IntPtr.Zero ? IntPtr.Zero : anchor;
            NativeMethods.SetWindowPos(hwnd, after, 0, 0, 0, 0, flags);
        }
    }

    public static bool IsAboveDesktop(IntPtr hwnd)
    {
        var progman = NativeMethods.FindWindow("Progman", "Program Manager");
        if (progman == IntPtr.Zero || hwnd == IntPtr.Zero) return false;
        for (var cursor = hwnd; cursor != IntPtr.Zero; cursor = NativeMethods.GetWindow(cursor, NativeMethods.GW_HWNDNEXT))
        {
            if (cursor == progman) return true;
        }
        return false;
    }

    public static string Describe(IntPtr hwnd)
    {
        var prev = NativeMethods.GetWindow(hwnd, NativeMethods.GW_HWNDPREV);
        var next = NativeMethods.GetWindow(hwnd, NativeMethods.GW_HWNDNEXT);
        return "prev=" + NativeMethods.ClassName(prev) + " next=" + NativeMethods.ClassName(next);
    }

    static void OnEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        var dispatcher = Application.Current?.Dispatcher;
        dispatcher?.BeginInvoke(PinAll, DispatcherPriority.Background);
    }
}
