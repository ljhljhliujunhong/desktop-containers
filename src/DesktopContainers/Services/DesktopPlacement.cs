using System.Windows;
using System.Windows.Threading;

namespace DesktopContainers;

/// <summary>顶层透明窗口保持在桌面上方、可见应用下方，不改变焦点或桌面窗口本身。</summary>
public static class DesktopPlacement
{
    const uint EventSystemForeground = 0x0003;
    const uint EventSystemMinimizeEnd = 0x0017;
    const uint EventObjectShow = 0x8002;
    const uint EventObjectReorder = 0x8004;
    const uint EventObjectUncloaked = 0x8018;
    const int DwmwaCloaked = 14;
    const uint PositionFlags = NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE
        | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER;

    static readonly NativeMethods.WinEventDelegate Callback = OnEvent;
    static readonly HashSet<IntPtr> Hwnds = new();
    static readonly List<IntPtr> Hooks = new();
    static DispatcherTimer? _repairTimer;
    static bool _applying;

    public static void Install()
    {
        if (Hooks.Count != 0) return;
        // 包括本进程的管理窗口；容器自己引发的事件在回调里过滤。
        Hook(EventSystemForeground, EventSystemForeground);
        Hook(EventSystemMinimizeEnd, EventSystemMinimizeEnd);
        Hook(EventObjectShow, EventObjectReorder);
        Hook(EventObjectUncloaked, EventObjectUncloaked);
    }

    static void Hook(uint first, uint last)
    {
        var hook = NativeMethods.SetWinEventHook(first, last, IntPtr.Zero, Callback, 0, 0, 0);
        if (hook != IntPtr.Zero) Hooks.Add(hook);
        else Log.Error("desktop event hook " + first);
    }

    public static void Stop()
    {
        _repairTimer?.Stop();
        foreach (var hook in Hooks) NativeMethods.UnhookWinEvent(hook);
        Hooks.Clear();
    }

    public static void Register(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        Hwnds.Add(hwnd);
        PinAll();
    }

    public static void Unregister(IntPtr hwnd) => Hwnds.Remove(hwnd);

    public static void PinAll()
    {
        if (_applying || Hwnds.Count == 0 || AppHost.IsExiting) return;
        _applying = true;
        try
        {
            var stack = ReadStack();
            foreach (var hwnd in Hwnds.ToArray())
            {
                if (!NativeMethods.IsWindow(hwnd)) { Hwnds.Remove(hwnd); continue; }
                if (stack.IsPlaced(hwnd)) continue;
                if (IsTopmost(hwnd))
                    NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0, PositionFlags);
                NativeMethods.SetWindowPos(hwnd, stack.InsertAfter, 0, 0, 0, 0, PositionFlags);
            }
        }
        finally { _applying = false; }
    }

    public static void Constrain(ref NativeMethods.WINDOWPOS pos)
    {
        if (_applying || !Hwnds.Contains(pos.hwnd) || (pos.flags & NativeMethods.SWP_NOZORDER) != 0) return;
        var after = ReadStack().InsertAfter;
        // 同步拦住 WPF/系统的提层请求，不等下一次前台切换才补救。
        pos.hwndInsertAfter = after == IntPtr.Zero && IsTopmost(pos.hwnd)
            ? NativeMethods.HWND_NOTOPMOST : after;
        pos.flags |= NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER;
        RequestPin();
    }

    static StackSnapshot ReadStack()
    {
        var windows = new List<IntPtr>();
        var seen = new HashSet<IntPtr>();
        var desktopIndex = -1;
        var appIndex = -1;
        var shell = NativeMethods.GetShellWindow();
        NativeMethods.GetWindowThreadProcessId(shell, out var shellProcess);
        for (var hwnd = NativeMethods.GetTopWindow(IntPtr.Zero);
             hwnd != IntPtr.Zero && windows.Count < 2048 && seen.Add(hwnd);
             hwnd = NativeMethods.GetWindow(hwnd, NativeMethods.GW_HWNDNEXT))
        {
            var index = windows.Count;
            windows.Add(hwnd);
            if (Hwnds.Contains(hwnd) || !NativeMethods.IsWindowVisible(hwnd)) continue;
            var desktop = hwnd == shell;
            if (!desktop && shellProcess != 0)
            {
                NativeMethods.GetWindowThreadProcessId(hwnd, out var process);
                desktop = process == shellProcess && NativeMethods.ClassName(hwnd) is "Progman" or "WorkerW";
            }
            if (desktop) { if (desktopIndex < 0) desktopIndex = index; continue; }
            if (IsTopmost(hwnd) || NativeMethods.IsIconic(hwnd)) continue;
            if (NativeMethods.DwmGetWindowAttribute(hwnd, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0) continue;
            if (!NativeMethods.GetWindowRect(hwnd, out var rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top) continue;
            appIndex = index;
        }

        var after = NativeMethods.HWND_BOTTOM;
        if (desktopIndex >= 0)
        {
            if (appIndex > desktopIndex)
                after = windows[appIndex]; // 桌面被临时抬高时，优先保证不遮住应用。
            else
            {
                after = IntPtr.Zero; // 只有没有可见普通应用阻挡时才允许普通窗口带的顶部。
                for (var index = desktopIndex - 1; index >= 0; index--)
                    if (!Hwnds.Contains(windows[index]) && !IsTopmost(windows[index]))
                    { after = windows[index]; break; }
            }
        }
        return new(windows, desktopIndex, appIndex, after);
    }

    sealed record StackSnapshot(List<IntPtr> Windows, int DesktopIndex, int AppIndex, IntPtr InsertAfter)
    {
        public bool IsPlaced(IntPtr hwnd)
        {
            var index = Windows.IndexOf(hwnd);
            if (index < 0 || IsTopmost(hwnd) || index <= AppIndex) return false;
            if (DesktopIndex < 0) return index == Windows.Count - 1;
            return AppIndex > DesktopIndex || index < DesktopIndex;
        }
    }

    public static bool IsTopmost(IntPtr hwnd) =>
        (NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64() & NativeMethods.WS_EX_TOPMOST) != 0;

    public static bool IsAboveDesktop(IntPtr hwnd)
    {
        var stack = ReadStack();
        var index = stack.Windows.IndexOf(hwnd);
        return index >= 0 && stack.DesktopIndex >= 0 && index < stack.DesktopIndex;
    }

    public static string Describe(IntPtr hwnd) =>
        "prev=" + NativeMethods.ClassName(NativeMethods.GetWindow(hwnd, NativeMethods.GW_HWNDPREV))
        + " next=" + NativeMethods.ClassName(NativeMethods.GetWindow(hwnd, NativeMethods.GW_HWNDNEXT))
        + " topmost=" + IsTopmost(hwnd);

    static void RequestPin()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted || AppHost.IsExiting) return;
        _repairTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background,
            (_, _) => { _repairTimer!.Stop(); PinAll(); }, dispatcher);
        if (!_repairTimer.IsEnabled) _repairTimer.Start();
    }

    static void OnEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (_applying || Hwnds.Contains(hwnd) || idChild != 0) return;
        // 顶层窗口重排由系统桌面的 OBJID_CLIENT 发出，而不是被移动窗口的 OBJID_WINDOW。
        if (idObject != 0 && !(eventType == EventObjectReorder && idObject == -4
            && hwnd == NativeMethods.GetDesktopWindow())) return;
        RequestPin();
    }
}
