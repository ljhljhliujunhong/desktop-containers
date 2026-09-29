using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DesktopContainers;

/// <summary>
/// 按显示器排列记住容器位置。拔掉外接屏时先冻结旧坐标，再在剩下的屏幕上散开。
/// 冻结之后、摆定之前，窗口被系统挪过的位置不能写回旧的那一份。
/// </summary>
public static class DisplayMemory
{
    const int SettleMs = 650;
    const int MaxLayouts = 8;
    const double PileRatio = 0.5;
    const int KnownSize = 80;
    const int SizeSlack = 8;

    static string _activeKey = "";
    static List<ContainerSpot>? _frozen;
    static bool _frozenCaptured;
    static bool _settling;
    static DispatcherTimer? _timer;

    public static void BindCurrent() => _activeKey = TopologyKey();

    public static void Stop()
    {
        _timer?.Stop();
        _frozen = null;
        _frozenCaptured = false;
        _settling = false;
    }

    public static void OnDisplayChanged()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;
        if (!dispatcher.CheckAccess())
        {
            try
            {
                dispatcher.Invoke(OnDisplayChanged);
            }
            catch (Exception ex)
            {
                Log.Error("reflow", ex);
            }
            return;
        }
        if (AppHost.IsExiting || AppHost.IsSmoke) return;
        FreezeOnce();
        ScheduleSettle();
    }

    public static void Note(ContainerModel model)
    {
        if (_frozenCaptured || _settling || AppHost.SuppressPersist) return;
        if (_activeKey.Length == 0 || AppHost.State == null || string.IsNullOrWhiteSpace(model.Id)) return;
        var layouts = AppHost.State.Document.ScreenLayouts ??= new List<ScreenLayout>();
        var layout = layouts.FirstOrDefault(item => item.Key == _activeKey);
        if (layout == null)
        {
            layout = new ScreenLayout { Key = _activeKey };
            layouts.Add(layout);
        }
        else
        {
            layouts.Remove(layout);
            layouts.Add(layout);
        }
        layout.Spots ??= new List<ContainerSpot>();
        var spot = layout.Spots.FirstOrDefault(item => item.Id == model.Id);
        if (spot == null)
        {
            spot = new ContainerSpot();
            layout.Spots.Add(spot);
        }
        Copy(spot, model);
        while (layouts.Count > MaxLayouts)
            layouts.RemoveAt(0);
    }

    public static void RepairIfPiled()
    {
        var started = false;
        try
        {
            if (AppHost.IsSmoke || AppHost.IsExiting || AppHost.State == null) return;
            if (_frozenCaptured || _settling || _timer is { IsEnabled: true }) return;
            var windows = AppHost.Windows.ToList();
            if (windows.Count < 1 || windows.Any(window => window.IsGesture)) return;
            var live = windows.Select(ReadLive).ToArray();
            if (!NeedsRepair(live)) return;

            started = true;
            _settling = true;
            _activeKey = TopologyKey();
            var works = WorkAreas();
            if (works.Length == 0) return;
            var fromSnapshot = TryUsable(_activeKey, windows, out var saved);
            var anchors = fromSnapshot
                ? AnchorsFor(windows, saved, true)
                : live;
            var fitted = DisplayReflow.Fit(anchors, works);
            ApplyFitted(windows, anchors, fitted, fromSnapshot ? saved : null);
            SaveSnapshot(_activeKey, CaptureModels());
            AppHost.State.Flush();
            Log.Info("reflow repair n=" + windows.Count);
        }
        catch (Exception ex)
        {
            Log.Error("reflow", ex);
        }
        finally
        {
            if (started) _settling = false;
        }
    }

    static void FreezeOnce()
    {
        if (_frozenCaptured || AppHost.State == null || _activeKey.Length == 0) return;
        _frozen = CaptureModels();
        _frozenCaptured = true;
        SaveSnapshot(_activeKey, _frozen);
        AppHost.State.RequestSave();
        Log.Info("reflow freeze key=" + _activeKey + " n=" + _frozen.Count);
    }

    static void ScheduleSettle()
    {
        if (Application.Current?.Dispatcher == null) return;
        if (_timer == null)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SettleMs) };
            _timer.Tick += OnTick;
        }
        _timer.Stop();
        _timer.Start();
    }

    static void OnTick(object? sender, EventArgs e)
    {
        _timer?.Stop();
        if (AppHost.IsExiting) return;
        if (AppHost.Windows.Any(window => window.IsGesture))
        {
            ScheduleSettle();
            return;
        }
        Settle();
    }

    static void Settle()
    {
        try
        {
            SettleCore();
        }
        catch (Exception ex)
        {
            Log.Error("reflow", ex);
        }
    }

    static void SettleCore()
    {
        var finished = true;
        try
        {
            if (AppHost.IsExiting || AppHost.State == null) return;
            var windows = AppHost.Windows.ToList();
            if (windows.Count == 0)
            {
                _activeKey = TopologyKey();
                return;
            }
            if (windows.Any(window => window.IsGesture))
            {
                finished = false;
                ScheduleSettle();
                return;
            }

            var newKey = TopologyKey();
            var live = windows.Select(ReadLive).ToArray();
            if (newKey == _activeKey && !NeedsRepair(live))
                return;

            var works = WorkAreas();
            if (works.Length == 0)
            {
                _activeKey = newKey;
                return;
            }

            _settling = true;
            _activeKey = newKey;
            var fromSnapshot = TryUsable(newKey, windows, out var saved);
            var anchors = AnchorsFor(windows, fromSnapshot ? saved : null, fromSnapshot);
            var fitted = DisplayReflow.Fit(anchors, works);
            ApplyFitted(windows, anchors, fitted, fromSnapshot ? saved : null);
            SaveSnapshot(newKey, CaptureModels());
            AppHost.State.Flush();
            Log.Info("reflow key=" + newKey + " snapshot=" + fromSnapshot + " n=" + windows.Count);
        }
        finally
        {
            if (finished)
            {
                _settling = false;
                _frozen = null;
                _frozenCaptured = false;
            }
        }
    }

    static void ApplyFitted(
        IReadOnlyList<ContainerWindow> windows,
        PixelRect[] anchors,
        PixelRect[] fitted,
        IReadOnlyList<ContainerSpot>? spots)
    {
        var needSecond = false;
        for (var i = 0; i < windows.Count; i++)
        {
            var window = windows[i];
            var live = ReadLive(window);
            var spot = FindSpot(spots, window.Model.Id);
            if (spot != null && Known(spot) && Near(fitted[i], spot))
            {
                window.RememberDip(spot.Width, spot.Height);
                continue;
            }
            if (!Known(spot) && fitted[i].W + SizeSlack >= live.W && fitted[i].H + SizeSlack >= live.H)
            {
                if (spot != null)
                    window.RememberDip(spot.Width, spot.Height);
                continue;
            }
            if (fitted[i].W + SizeSlack < live.W || fitted[i].H + SizeSlack < live.H)
            {
                window.FitInside(fitted[i].W, fitted[i].H);
                needSecond = true;
            }
        }

        foreach (var window in windows)
            window.UpdateLayout();

        var positions = fitted;
        if (needSecond)
        {
            var second = new PixelRect[windows.Count];
            for (var i = 0; i < windows.Count; i++)
            {
                var live = ReadLive(windows[i]);
                second[i] = new PixelRect(anchors[i].X, anchors[i].Y, Math.Max(KnownSize, live.W), Math.Max(KnownSize, live.H));
            }
            positions = DisplayReflow.Fit(second, WorkAreas(), false);
        }

        AppHost.SuppressPersist = true;
        try
        {
            for (var i = 0; i < windows.Count; i++)
                windows[i].ApplyPixelBounds(positions[i].X, positions[i].Y, positions[i].W, positions[i].H, false);
            DesktopPlacement.PinAll();
        }
        finally
        {
            AppHost.SuppressPersist = false;
        }
        foreach (var window in windows)
            window.CommitBounds();
    }

    static PixelRect[] AnchorsFor(IReadOnlyList<ContainerWindow> windows, IReadOnlyList<ContainerSpot>? spots, bool fromSnapshot)
    {
        var result = new PixelRect[windows.Count];
        for (var i = 0; i < windows.Count; i++)
        {
            var window = windows[i];
            var spot = fromSnapshot ? FindSpot(spots, window.Model.Id) : null;
            result[i] = spot != null ? RectFromSpot(spot, window) : RectFromFrozenOrLive(window);
        }
        return result;
    }

    static PixelRect RectFromSpot(ContainerSpot spot, ContainerWindow window)
    {
        var live = ReadLive(window);
        int width;
        int height;
        if (Known(spot))
        {
            width = spot.PixelWidth;
            height = spot.PixelHeight;
        }
        else if (live.W >= KnownSize && live.H >= KnownSize)
        {
            width = live.W;
            height = live.H;
        }
        else
        {
            width = Math.Max(KnownSize, Math.Max(live.W, spot.PixelWidth));
            height = Math.Max(KnownSize, Math.Max(live.H, spot.PixelHeight));
        }
        return new PixelRect(spot.PixelX, spot.PixelY, width, height);
    }

    static PixelRect RectFromFrozenOrLive(ContainerWindow window)
    {
        var live = ReadLive(window);
        var spot = FindSpot(_frozen, window.Model.Id);
        if (spot == null)
        {
            if (live.W >= KnownSize && live.H >= KnownSize) return live;
            var model = window.Model;
            var width = model.PixelWidth >= KnownSize ? model.PixelWidth : 440;
            var height = model.PixelHeight >= KnownSize ? model.PixelHeight : 320;
            var x = model.HasPixelPosition ? model.PixelX : live.X;
            var y = model.HasPixelPosition ? model.PixelY : live.Y;
            return new PixelRect(x, y, width, height);
        }

        int w;
        int h;
        if (live.W >= KnownSize && live.H >= KnownSize)
        {
            w = live.W;
            h = live.H;
        }
        else if (Known(spot))
        {
            w = spot.PixelWidth;
            h = spot.PixelHeight;
        }
        else
        {
            w = KnownSize;
            h = KnownSize;
        }
        return new PixelRect(spot.PixelX, spot.PixelY, w, h);
    }

    static bool TryUsable(string key, IReadOnlyList<ContainerWindow> windows, out List<ContainerSpot> spots)
    {
        spots = Clone(Find(key));
        if (spots.Count == 0) return false;
        var matched = new List<PixelRect>();
        foreach (var window in windows)
        {
            var spot = FindSpot(spots, window.Model.Id);
            if (spot == null) continue;
            matched.Add(RectFromSpot(spot, window));
        }
        if (matched.Count == 0 || DisplayReflow.HasHeavyOverlap(matched, PileRatio)) return false;
        var works = WorkAreas();
        var inside = matched.Count(rect => DisplayReflow.MostlyInside(rect, works));
        return inside / (double)matched.Count >= PileRatio;
    }

    static bool NeedsRepair(IReadOnlyList<PixelRect> live)
    {
        if (live.Count == 0) return false;
        var works = WorkAreas();
        foreach (var rect in live)
        {
            if (!DisplayReflow.MostlyInside(rect, works)) return true;
        }
        return DisplayReflow.HasHeavyOverlap(live, PileRatio);
    }

    static List<ContainerSpot> CaptureModels()
    {
        var spots = new List<ContainerSpot>();
        foreach (var window in AppHost.Windows)
        {
            var spot = new ContainerSpot();
            Copy(spot, window.Model);
            spots.Add(spot);
        }
        return spots;
    }

    static void SaveSnapshot(string key, List<ContainerSpot> spots)
    {
        if (key.Length == 0 || AppHost.State == null) return;
        var layouts = AppHost.State.Document.ScreenLayouts ??= new List<ScreenLayout>();
        layouts.RemoveAll(item => item.Key == key);
        layouts.Add(new ScreenLayout { Key = key, Spots = Clone(spots) });
        while (layouts.Count > MaxLayouts)
            layouts.RemoveAt(0);
    }

    static List<ContainerSpot> Find(string key)
    {
        var layouts = AppHost.State?.Document.ScreenLayouts;
        if (layouts == null || key.Length == 0) return [];
        for (var i = layouts.Count - 1; i >= 0; i--)
        {
            var layout = layouts[i];
            if (layout?.Key == key && layout.Spots != null)
                return layout.Spots;
        }
        return [];
    }

    static ContainerSpot? FindSpot(IReadOnlyList<ContainerSpot>? spots, string id)
    {
        if (spots == null || string.IsNullOrWhiteSpace(id)) return null;
        for (var i = 0; i < spots.Count; i++)
        {
            if (spots[i].Id == id) return spots[i];
        }
        return null;
    }

    static void Copy(ContainerSpot spot, ContainerModel model)
    {
        spot.Id = model.Id;
        spot.PixelX = model.PixelX;
        spot.PixelY = model.PixelY;
        spot.PixelWidth = model.PixelWidth;
        spot.PixelHeight = model.PixelHeight;
        spot.X = model.X;
        spot.Y = model.Y;
        spot.Width = model.Width;
        spot.Height = model.Height;
    }

    static List<ContainerSpot> Clone(List<ContainerSpot> spots) => spots.Select(CloneSpot).ToList();

    static ContainerSpot CloneSpot(ContainerSpot spot) => new()
    {
        Id = spot.Id,
        PixelX = spot.PixelX,
        PixelY = spot.PixelY,
        PixelWidth = spot.PixelWidth,
        PixelHeight = spot.PixelHeight,
        X = spot.X,
        Y = spot.Y,
        Width = spot.Width,
        Height = spot.Height
    };

    static PixelRect ReadLive(ContainerWindow window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero && NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return new PixelRect(rect.Left, rect.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top));
        }
        var model = window.Model;
        var width = model.PixelWidth >= KnownSize ? model.PixelWidth : 440;
        var height = model.PixelHeight >= KnownSize ? model.PixelHeight : 320;
        if (model.HasPixelPosition)
            return new PixelRect(model.PixelX, model.PixelY, width, height);
        return new PixelRect((int)Math.Round(model.X), (int)Math.Round(model.Y), width, height);
    }

    static PixelRect[] WorkAreas()
    {
        return System.Windows.Forms.Screen.AllScreens
            .Select(screen => screen.WorkingArea)
            .Where(area => area.Width > 0 && area.Height > 0)
            .OrderBy(area => area.Top)
            .ThenBy(area => area.Left)
            .Select(area => new PixelRect(area.Left, area.Top, area.Width, area.Height))
            .ToArray();
    }

    static string TopologyKey()
    {
        var parts = System.Windows.Forms.Screen.AllScreens
            .OrderBy(screen => screen.Bounds.Top)
            .ThenBy(screen => screen.Bounds.Left)
            .ThenBy(screen => screen.DeviceName ?? "", StringComparer.OrdinalIgnoreCase)
            .Select(screen =>
            {
                var bounds = screen.Bounds;
                var tag = screen.Primary ? "P" : "S";
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}{1},{2},{3}x{4}",
                    tag,
                    bounds.Left,
                    bounds.Top,
                    bounds.Width,
                    bounds.Height);
            });
        return string.Join("|", parts);
    }

    static bool Known(ContainerSpot? spot) =>
        spot != null && spot.PixelWidth >= KnownSize && spot.PixelHeight >= KnownSize;

    static bool Near(PixelRect fitted, ContainerSpot spot) =>
        Math.Abs(fitted.W - spot.PixelWidth) <= SizeSlack && Math.Abs(fitted.H - spot.PixelHeight) <= SizeSlack;
}
