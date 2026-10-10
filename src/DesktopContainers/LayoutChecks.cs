using System.Windows.Interop;

namespace DesktopContainers;

public static class LayoutChecks
{
    public static void CheckGeometry()
    {
        var main = new LayoutMonitor { Device = "main", Primary = true, Width = 2560, Height = 1440 };
        var small = new LayoutMonitor { Device = "main", Primary = true, Width = 1920, Height = 1080 };
        var left = new LayoutMonitor { Device = "left", X = -1920, Width = 1920, Height = 1080 };
        var pinnedRight = new PixelRect(2335, 0, 225, 412);
        var projected = LayoutProjection.Project(pinnedRight, [main], [small]);
        Require(projected.X == 1695 && projected.Y == 0 && projected.W == 225, "换分辨率后右侧容器应保持靠右");
        var scaledDpi = LayoutProjection.Project(pinnedRight, [main], [main], 450, 824);
        Require(scaledDpi.X == 2110 && scaledDpi.Y == 0 && scaledDpi.W == 450, "DPI 改变后应按实际窗口大小保留靠右位置");
        var fromLeft = LayoutProjection.Project(new PixelRect(-1800, 40, 200, 300), [left, main], [small]);
        Require(fromLeft.X >= 0 && fromLeft.X + fromLeft.W <= 1920, "拔掉左侧屏幕后容器应映射到剩余屏幕");
        var works = new[] { new PixelRect(0, 0, 2560, 1400) };
        var edge = new PixelRect(1799, -9, 615, 162);
        Require(LayoutProjection.EnsureVisible(edge, works) == edge, "顶部阴影越界不应改变布局");
        var off = LayoutProjection.EnsureVisible(new PixelRect(-8000, 0, 200, 300), works);
        Require(off.X >= 0 && off.X + off.W <= 2560, "不可操作的容器应回到屏幕内");
        var snapshot = new ScreenLayout
        {
            Key = "original", Monitors = [main],
            Spots = [new ContainerSpot { Id = "a", PixelX = 44, Width = 120, Height = 180 }]
        };
        var copy = LayoutProjection.Clone(snapshot);
        copy.Spots[0].PixelX = 900;
        copy.Monitors[0].Width = 800;
        Require(snapshot.Spots[0].PixelX == 44 && snapshot.Monitors[0].Width == 2560, "手动记忆不能与自动布局共享可变对象");
        CheckGroupProjection();
    }

    static void CheckGroupProjection()
    {
        var source = new ScreenLayout
        {
            Key = "P0,0,2560x1440@96",
            Monitors = [new LayoutMonitor { Device = "main", Primary = true, Width = 2560, Height = 1440 }],
            Spots =
            [
                Spot("AI", 1484, 165, 381, 162), Spot("VibeCoding", 1250, 0, 615, 162),
                Spot("CodingTools", 1859, 0, 196, 262), Spot("Media", 2049, 0, 151, 362),
                Spot("Game", 2194, 0, 147, 362), Spot("Tools", 2335, 0, 225, 412)
            ]
        };
        var small = new LayoutMonitor { Device = "main", Primary = true, Width = 1707, Height = 1067 };
        var projected = LayoutProjection.Adapt(source, "small", [small], [new PixelRect(0, 0, 1707, 1031)], false);
        for (var i = 0; i < source.Spots.Count; i++)
        {
            Require(projected.Spots[i].PixelX == source.Spots[i].PixelX - 853
                && projected.Spots[i].PixelY == source.Spots[i].PixelY
                && projected.Spots[i].Width == source.Spots[i].Width && projected.Spots[i].Scale == 1,
                "右侧整组在小屏能放下时，应整体靠右移动，不能拆散或换列");
        }
        var tiny = new LayoutMonitor { Device = "main", Primary = true, Width = 800, Height = 600 };
        var shrunken = LayoutProjection.Adapt(source, "tiny", [tiny], [new PixelRect(0, 0, 800, 560)], false);
        var factor = 800.0 / 1310;
        for (var i = 0; i < source.Spots.Count; i++)
        {
            Require(Math.Abs(shrunken.Spots[i].Scale - factor) < 0.0001
                && shrunken.Spots[i].PixelX == (int)Math.Round((source.Spots[i].PixelX - 1250) * factor)
                && shrunken.Spots[i].PixelY == (int)Math.Round(source.Spots[i].PixelY * factor),
                "放不下时，每个容器及其相对位置应使用同一缩放比例");
        }
        Require(source.Spots[0].PixelX == 1484 && source.Spots[0].Scale == 1, "适配不能覆盖大屏的手动记忆");
        var highDpi = new LayoutMonitor { Device = "main", Primary = true, Width = 2560, Height = 1600, Dpi = 144 };
        var physical = LayoutProjection.Adapt(source, "small-150-percent", [highDpi], [new PixelRect(0, 0, 2560, 1544)], false);
        for (var i = 0; i < source.Spots.Count; i++)
        {
            Require(physical.Spots[i].PixelX == 595 + (int)Math.Round((source.Spots[i].PixelX - 1250) * 1.5)
                && physical.Spots[i].PixelY == (int)Math.Round(source.Spots[i].PixelY * 1.5)
                && physical.Spots[i].Scale == 1 && physical.Spots[i].Width == source.Spots[i].Width,
                "150% DPI 的小屏应保持相同视觉排列，不能把 DIP 当成物理像素");
        }
        var returned = LayoutProjection.Adapt(source, source.Key, source.Monitors, [new PixelRect(0, 0, 2560, 1400)], true);
        Require(returned.Spots.Zip(source.Spots).All(pair => pair.First.PixelX == pair.Second.PixelX
            && pair.First.PixelY == pair.Second.PixelY && pair.First.Scale == pair.Second.Scale), "接回大屏应恢复原始排列");
    }

    static ContainerSpot Spot(string id, int x, int y, int width, int height) => new()
    {
        Id = id, PixelX = x, PixelY = y, PixelWidth = width, PixelHeight = height, Width = width, Height = height
    };

    public static async Task CheckWindows()
    {
        var windows = AppHost.Windows.ToList();
        Require(windows.Count == 2, "布局验证需要两只测试容器");
        DisplayMemory.Stop();
        windows[0].ApplyPixelBounds(48, 64, 0, 0, false);
        windows[1].ApplyPixelBounds(520, 64, 0, 0, false);
        var first = LayoutProfiles.Save("工作");
        var firstScreen = LayoutProjection.Clone(first.Screens.Single());
        windows[0].ApplyPixelBounds(180, 320, 0, 0, false);
        windows[1].ApplyPixelBounds(700, 320, 0, 0, false);
        var second = LayoutProfiles.Save("娱乐");
        Require(first.Screens[0].Spots[0].PixelX == firstScreen.Spots[0].PixelX, "拖动后不能覆盖另一套记忆");
        DisplayMemory.ApplyProfile(first);
        AssertPositions(firstScreen);
        DisplayMemory.ApplyProfile(second);
        AssertPositions(second.Screens.Single());
        // 模拟同一套布局已在另一种屏幕下手动保存；更新当前屏幕必须保留它。
        var other = LayoutProjection.Clone(firstScreen);
        other.Key = "P0,0,1366x768";
        if (other.Key == DisplayMemory.CurrentKey) other.Key = "P0,0,800x600";
        first.Screens.Add(other);
        LayoutProfiles.Save(first.Name, first.Id);
        Require(first.Screens.Count == 2 && first.Screens.Any(screen => screen.Key == other.Key), "更新只替换当前屏幕的记忆");
        LayoutProfiles.Rename(first, "日常工作");
        var rejected = false;
        try { LayoutProfiles.Rename(first, second.Name); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected && first.Name == "日常工作", "重复名称应被拒绝且不丢失原名称");
        LayoutProfiles.Delete(second);
        Require(AppHost.Windows.Count == 2 && AppHost.State.Document.Containers.Count == 2, "删除记忆不能删除容器");
        LayoutProfiles.SetStartup(first.Id);
        var expected = LayoutProjection.Clone(first.Screens.Last(screen => screen.Key == DisplayMemory.CurrentKey));
        // 模拟应用完全重开。初始化和系统临时挪窗都不能先覆盖磁盘上的位置。
        var document = AppHost.Store.Load();
        var oldSuppress = AppHost.SuppressPersist;
        AppHost.SuppressPersist = true;
        foreach (var window in windows) window.Close();
        AppHost.SuppressPersist = oldSuppress;
        AppHost.State = new AppState(AppHost.Store, document);
        DisplayMemory.BindCurrent();
        foreach (var model in document.Containers) AppHost.OpenContainer(model, false);
        foreach (var window in AppHost.Windows)
        {
            window.ApplyPixelBounds(20, 20, 0, 0, false);
            window.CommitBounds();
        }
        foreach (var spot in expected.Spots)
            Require(AppHost.State.Find(spot.Id)?.PixelX == spot.PixelX, "启动期临时 HWND 坐标不能污染模型");
        DisplayMemory.RestoreStartup();
        await Task.Delay(1800);
        Require(!DisplayMemory.IsBusy, "启动恢复应在屏幕稳定后完成");
        AssertPositions(expected);
        var reloaded = AppHost.Store.Load();
        Require(reloaded.LayoutProfiles.Count == 1 && reloaded.StartupLayoutId == first.Id
            && reloaded.LayoutProfiles[0].Screens.Count == 2, "命名布局和开机选择必须跨重启持久化");
        // 同屏故意叠放也必须原样恢复，不能自动散开。
        foreach (var window in AppHost.Windows) window.ApplyPixelBounds(100, 100, 0, 0, false);
        var overlap = LayoutProfiles.Save("叠放");
        foreach (var window in AppHost.Windows) window.ApplyPixelBounds(600, 400, 0, 0, false);
        DisplayMemory.ApplyProfile(overlap);
        AssertPositions(overlap.Screens.Single());
        LayoutProfiles.Delete(overlap);
        DisplayMemory.ApplyProfile(AppHost.State.Document.LayoutProfiles[0]);
        CheckScaledWindows();
    }

    static void CheckScaledWindows()
    {
        var original = AppHost.State.Document.LayoutProfiles[0];
        var crossScreen = LayoutProfiles.Save("跨屏验证");
        var saved = LayoutProjection.Clone(crossScreen.Screens.Single());
        var monitor = saved.Monitors.First(item => item.Primary);
        monitor.Width *= 2;
        saved.Key = "synthetic-wide";
        saved.Spots[0].PixelX = monitor.X;
        saved.Spots[1].PixelX = monitor.X + monitor.Width - saved.Spots[1].PixelWidth;
        saved.Spots.ForEach(spot => spot.PixelY = 40);
        crossScreen.Screens = [saved];
        var preview = DisplayMemory.PreviewLayout(saved);
        DisplayMemory.ApplyProfile(crossScreen);
        AssertPositions(preview);
        Require(AppHost.Windows.All(window => window.Model.LayoutScale < 1), "跨屏缩小应缩放整个容器，保持图标列数");
        DisplayMemory.ApplyProfile(crossScreen);
        AssertPositions(preview);
        Require(crossScreen.Screens[0].Spots.All(spot => spot.Scale == 1), "连续点击使用不能累计缩放或改写记忆");
        LayoutProfiles.Delete(crossScreen);
        DisplayMemory.ApplyProfile(original);
        Require(AppHost.Windows.All(window => window.Model.LayoutScale == 1), "切回原布局应恢复正常尺寸");
    }

    static void AssertPositions(ScreenLayout layout)
    {
        foreach (var spot in layout.Spots)
        {
            var window = AppHost.FindWindow(spot.Id)!;
            Require(NativeMethods.GetWindowRect(new WindowInteropHelper(window).Handle, out var rect)
                && Math.Abs(rect.Left - spot.PixelX) <= 1 && Math.Abs(rect.Top - spot.PixelY) <= 1,
                "恢复后位置应与已保存的位置一致");
            Require(Math.Abs(window.Width - spot.Width) <= 1 && Math.Abs(window.Height - spot.Height) <= 1,
                "恢复后尺寸应与已保存的尺寸一致");
        }
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("layout check: " + message);
    }
}
