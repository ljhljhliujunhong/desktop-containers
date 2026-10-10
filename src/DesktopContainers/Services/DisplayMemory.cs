using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DesktopContainers;

/// <summary>系统调整窗口期间冻结模型；恢复完成之前，临时坐标不能写回记忆。</summary>
public static class DisplayMemory
{
    const int MaxLayouts = 8;
    static string _activeKey = "";
    static ScreenLayout? _frozen;
    static bool _restoring;
    static bool _startup;
    static string _observedKey = "";
    static DispatcherTimer? _timer;

    public static bool CanPersist => !_restoring && _timer is not { IsEnabled: true };
    public static bool IsBusy => !CanPersist || AppHost.Windows.Any(window => window.IsGesture);
    public static string CurrentKey => TopologyKey();

    public static void BindCurrent()
    {
        Stop();
        var document = AppHost.State.Document;
        _activeKey = string.IsNullOrEmpty(document.LastScreenKey) ? TopologyKey() : document.LastScreenKey;
        _frozen = SnapshotModels(_activeKey);
        _restoring = true;
        _startup = true;
    }

    public static void RestoreStartup() => ScheduleSettle();

    public static void OnDpiChanged()
    {
        // 用户拖到另一块 DPI 不同的屏幕时，整套屏幕拓扑并没有变化。
        // 这种移动继续由拖动结束时保存，不能触发整桌恢复。
        if (_activeKey != TopologyKey()) OnDisplayChanged();
    }

    public static void Stop()
    {
        _timer?.Stop();
        _frozen = null;
        _restoring = false;
        _startup = false;
        _observedKey = "";
        _activeKey = "";
    }

    public static void OnDisplayChanged()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || AppHost.IsExiting || AppHost.IsSmoke) return;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(OnDisplayChanged);
            return;
        }
        if (!_restoring)
        {
            // 只读模型；显示通知到达时 HWND 可能已经被 Windows 移动。
            _frozen = SnapshotModels(_activeKey);
            _restoring = true;
            if (_activeKey.Length > 0) SaveSnapshot(_frozen);
            AppHost.State.RequestSave();
        }
        _observedKey = "";
        ScheduleSettle();
    }

    public static void Note(ContainerModel model)
    {
        if (!CanPersist || AppHost.SuppressPersist || AppHost.State == null) return;
        var key = TopologyKey();
        if (_activeKey.Length == 0) _activeKey = key;
        if (key != _activeKey)
        {
            OnDisplayChanged();
            return;
        }
        var layouts = AppHost.State.Document.ScreenLayouts;
        var layout = layouts.LastOrDefault(item => item.Key == key);
        if (layout == null)
        {
            layout = new ScreenLayout { Key = key, Monitors = Monitors() };
            layouts.Add(layout);
        }
        layout.Spots.RemoveAll(spot => spot.Id == model.Id);
        layout.Spots.Add(Spot(model));
        layouts.Remove(layout);
        layouts.Add(layout);
        while (layouts.Count > MaxLayouts) layouts.RemoveAt(0);
        AppHost.State.Document.LastScreenKey = key;
    }

    // 在 PersistBounds 写模型之前检测拓扑，防止抢在显示通知之前污染旧坐标。
    public static bool CheckBeforePersist()
    {
        if (!CanPersist) return false;
        if (!AppHost.IsSmoke && _activeKey.Length > 0 && _activeKey != TopologyKey())
        {
            OnDisplayChanged();
            return false;
        }
        return true;
    }

    public static ScreenLayout CaptureCurrent()
    {
        if (IsBusy) throw new InvalidOperationException("屏幕正在调整，请稍后再试。");
        foreach (var window in AppHost.Windows) window.CommitBounds();
        return SnapshotModels(TopologyKey());
    }

    public static void ApplyProfile(LayoutProfile profile)
    {
        if (IsBusy) throw new InvalidOperationException("屏幕正在调整，请稍后再试。");
        var source = Choose(profile.Screens, TopologyKey());
        if (source == null) throw new InvalidOperationException("这个布局没有可用的位置。");
        var oldActive = AppHost.State.Document.ActiveLayoutId;
        var before = CaptureCurrent();
        _restoring = true;
        try
        {
            Apply(source);
            _activeKey = TopologyKey();
            AppHost.State.Document.ActiveLayoutId = profile.Id;
            SaveSnapshot(SnapshotModels(_activeKey));
            AppHost.State.Document.LastScreenKey = _activeKey;
            AppHost.State.Flush();
        }
        catch
        {
            AppHost.State.Document.ActiveLayoutId = oldActive;
            Apply(before);
            SaveSnapshot(before);
            throw;
        }
        finally { _restoring = false; }
    }

    static void ScheduleSettle()
    {
        if (_timer == null)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            _timer.Tick += OnTick;
        }
        _timer.Stop();
        _timer.Start();
    }

    static void OnTick(object? sender, EventArgs e)
    {
        _timer?.Stop();
        if (AppHost.IsExiting) return;
        var key = TopologyKey();
        if (_observedKey != key || AppHost.Windows.Any(window => window.IsGesture))
        {
            _observedKey = key;
            ScheduleSettle();
            return;
        }
        try
        {
            _restoring = true;
            var document = AppHost.State.Document;
            var profileId = _startup ? document.StartupLayoutId : document.ActiveLayoutId;
            var profile = document.LayoutProfiles.FirstOrDefault(item => item.Id == profileId);
            var source = !_startup && key == _activeKey ? _frozen
                : profile == null ? null : Choose(profile.Screens, key);
            source ??= FindForScreen(document.ScreenLayouts, key);
            source ??= _frozen;
            if (source != null) Apply(source);
            _activeKey = key;
            document.LastScreenKey = key;
            if (_startup) document.ActiveLayoutId = profile?.Id;
            // 已记住的精确坐标不因工作区暂时变小而被覆盖。
            if (source?.Key != key || !document.ScreenLayouts.Any(layout => layout.Key == key))
                SaveSnapshot(SnapshotModels(key));
            AppHost.State.Flush();
            Log.Info("layout restore key=" + key + " profile=" + (profile?.Name ?? "auto"));
            AppHost.Manager?.RefreshLayoutPage();
        }
        catch (Exception ex) { Log.Error("layout restore", ex); }
        finally
        {
            _restoring = false;
            _startup = false;
            _frozen = null;
        }
    }

    static void Apply(ScreenLayout source)
    {
        var windows = AppHost.Windows.ToList();
        var works = WorkAreas();
        if (works.Length == 0) throw new InvalidOperationException("没有可用的屏幕。");
        var plan = PreviewLayout(source);
        var spots = plan.Spots.ToDictionary(spot => spot.Id);
        var previousSuppress = AppHost.SuppressPersist;
        AppHost.SuppressPersist = true;
        try
        {
            foreach (var window in windows)
            {
                if (spots.TryGetValue(window.Model.Id, out var spot))
                {
                    // 位置、列数和整体缩放都来自同一份计划，预览也使用这份计划。
                    window.ApplyPixelBounds(spot.PixelX, spot.PixelY, spot.PixelWidth, spot.PixelHeight, false);
                    window.RestoreLayoutSize(spot.Width, spot.Height, spot.Scale);
                    window.UpdateLayout();
                    window.ApplyPixelBounds(spot.PixelX, spot.PixelY, spot.PixelWidth, spot.PixelHeight, false);
                }
                else
                {
                    var rect = LayoutProjection.EnsureVisible(ReadLive(window), works);
                    window.ApplyPixelBounds(rect.X, rect.Y, rect.W, rect.H, false);
                }
                UpdateModel(window);
            }
            DesktopPlacement.PinAll();
        }
        finally { AppHost.SuppressPersist = previousSuppress; }
    }

    public static ScreenLayout PreviewLayout(ScreenLayout source)
    {
        var key = TopologyKey();
        return LayoutProjection.Adapt(source, key, Monitors(), WorkAreas(), source.Key == key || IsLegacyMatch(source.Key, key));
    }

    static void UpdateModel(ContainerWindow window)
    {
        var rect = ReadLive(window);
        var model = window.Model;
        model.PixelX = rect.X;
        model.PixelY = rect.Y;
        model.PixelWidth = rect.W;
        model.PixelHeight = rect.H;
        model.HasPixelPosition = true;
        model.X = window.Left;
        model.Y = window.Top;
        model.Width = window.Width;
        model.Height = window.Height;
        model.MonitorDevice = MonitorGuard.DeviceName(window);
    }

    static ScreenLayout SnapshotModels(string key) => new()
    {
        Key = key,
        Spots = AppHost.State.Document.Containers.Where(model => model.HasPixelPosition).Select(Spot).ToList(),
        Monitors = AppHost.State.Document.ScreenLayouts.LastOrDefault(layout => layout.Key == key)?.Monitors is { Count: > 0 } monitors
            ? LayoutProjection.CloneMonitors(monitors) : Monitors()
    };

    static void SaveSnapshot(ScreenLayout layout)
    {
        var layouts = AppHost.State.Document.ScreenLayouts;
        layouts.RemoveAll(item => item.Key == layout.Key);
        layouts.Add(LayoutProjection.Clone(layout));
        while (layouts.Count > MaxLayouts) layouts.RemoveAt(0);
    }

    public static ScreenLayout? Choose(List<ScreenLayout> layouts, string key) =>
        FindForScreen(layouts, key) ?? layouts.LastOrDefault();

    static ScreenLayout? FindForScreen(List<ScreenLayout> layouts, string key) =>
        layouts.LastOrDefault(layout => layout.Key == key)
        ?? layouts.LastOrDefault(layout => IsLegacyMatch(layout.Key, key));

    static bool IsLegacyMatch(string oldKey, string key) => !oldKey.Contains('@')
        && oldKey == string.Join("|", key.Split('|').Select(part => part.Split('@')[0]));

    static ContainerSpot Spot(ContainerModel model) => new()
    {
        Id = model.Id, PixelX = model.PixelX, PixelY = model.PixelY,
        PixelWidth = model.PixelWidth, PixelHeight = model.PixelHeight,
        X = model.X, Y = model.Y, Width = model.Width, Height = model.Height, Scale = model.LayoutScale
    };

    static PixelRect Rect(ContainerSpot spot) => new(spot.PixelX, spot.PixelY,
        Math.Max(1, spot.PixelWidth), Math.Max(1, spot.PixelHeight));

    static PixelRect ReadLive(ContainerWindow window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero && NativeMethods.GetWindowRect(hwnd, out var rect))
            return new PixelRect(rect.Left, rect.Top, Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top));
        return new PixelRect(window.Model.PixelX, window.Model.PixelY,
            Math.Max(1, window.Model.PixelWidth), Math.Max(1, window.Model.PixelHeight));
    }

    public static List<LayoutMonitor> Monitors() => System.Windows.Forms.Screen.AllScreens
        .OrderBy(screen => screen.Bounds.Top).ThenBy(screen => screen.Bounds.Left)
        .Select(screen => new LayoutMonitor
        {
            Device = screen.DeviceName, Primary = screen.Primary,
            Dpi = MonitorDpi(screen.Bounds.Left, screen.Bounds.Top),
            X = screen.Bounds.Left, Y = screen.Bounds.Top,
            Width = screen.Bounds.Width, Height = screen.Bounds.Height
        }).ToList();

    static PixelRect[] WorkAreas() => System.Windows.Forms.Screen.AllScreens
        .Select(screen => screen.WorkingArea).Where(area => area.Width > 0 && area.Height > 0)
        .Select(area => new PixelRect(area.Left, area.Top, area.Width, area.Height)).ToArray();

    static string TopologyKey() => string.Join("|", Monitors().Select(monitor => string.Format(
        CultureInfo.InvariantCulture, "{0}{1},{2},{3}x{4}@{5}", monitor.Primary ? "P" : "S",
        monitor.X, monitor.Y, monitor.Width, monitor.Height, monitor.Dpi)));

    static uint MonitorDpi(int x, int y)
    {
        var monitor = NativeMethods.MonitorFromPoint(new NativeMethods.POINT { X = x, Y = y }, 2);
        return NativeMethods.GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 && dpi >= 48 ? dpi : 96;
    }
}
