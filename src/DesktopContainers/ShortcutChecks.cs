using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DesktopContainers;

internal static class ShortcutChecks
{
    public static void CheckCleanup()
    {
        var original = AppHost.Shortcuts;
        var root = Path.Combine(AppHost.DataRoot, "shortcut-check");
        var store = new LayoutStore(root);
        var shortcuts = new ShortcutService(root);
        AppHost.Shortcuts = shortcuts;
        try
        {
            var document = new LayoutDocument();
            var container = new ContainerModel { Id = "cleanup", Locked = true };
            document.Containers.Add(container);
            var state = new AppState(store, document);
            var now = DateTime.UtcNow;
            var target = Path.Combine(root, "installed", "application.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, "probe");
            var app = Add(container, target);
            var url = Add(container, "https://example.com/", ".url");
            var shell = Add(container, "");
            var network = Add(container, @"\\offline.invalid\share\application.exe");
            var drive = Enumerable.Range('D', 'Z' - 'D' + 1).Select(letter => (char)letter + @":\")
                .FirstOrDefault(path => !Directory.Exists(path));
            var offline = drive == null ? null : Add(container, drive + "application.exe");

            state.Reconcile(now);
            Require(!app.Missing && container.Apps.Contains(app), "existing target retained");
            Directory.Delete(Path.GetDirectoryName(target)!, true);
            state.Reconcile(now.AddSeconds(1));
            state.Reconcile(now.AddSeconds(20));
            Require(container.Apps.Contains(app), "uninstall grace period");
            state.Reconcile(now.AddSeconds(32));
            Require(!container.Apps.Contains(app) && !File.Exists(shortcuts.PathOf(app)), "locked uninstall cleanup");
            Require(container.Apps.Contains(url) && container.Apps.Contains(shell)
                && container.Apps.Contains(network) && (offline == null || container.Apps.Contains(offline)),
                "URL, shell, network and offline drive retained");
            Require(!store.Load().Containers[0].Apps.Any(item => item.Id == app.Id), "cleanup persisted");

            var transient = Add(container, target);
            state.Reconcile(now.AddSeconds(40));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, "restored");
            state.Reconcile(now.AddSeconds(50));
            Require(!transient.Missing && container.Apps.Contains(transient), "temporary absence recovered");
            File.Delete(target);
            state.Reconcile(now.AddSeconds(60));
            state.Reconcile(now.AddSeconds(80));
            Require(container.Apps.Contains(transient), "recovery reset grace period");
            state.Reconcile(now.AddSeconds(91));
            Require(!container.Apps.Contains(transient), "second uninstall cleaned up");

            File.Delete(shortcuts.PathOf(url));
            state.Reconcile(now.AddSeconds(100));
            state.Reconcile(now.AddSeconds(131));
            Require(!container.Apps.Contains(url), "deleted stored shortcut cleaned up");

            var startup = Add(container, target);
            state.Flush();
            state = AppState.Load(store);
            state.Reconcile(now.AddSeconds(140));
            Require(state.Document.Containers[0].Apps.Any(item => item.Id == startup.Id), "startup grace period");
            state.Reconcile(now.AddSeconds(171));
            Require(!store.Load().Containers[0].Apps.Any(item => item.Id == startup.Id), "startup cleanup persisted");
        }
        finally
        {
            AppHost.Shortcuts = original;
        }

        AppEntry Add(ContainerModel owner, string target, string extension = ".lnk")
        {
            var id = Guid.NewGuid().ToString("N");
            var entry = new AppEntry { Id = id, FileName = id + extension, DisplayName = "probe", TargetPath = target };
            File.WriteAllText(shortcuts.PathOf(entry), "[InternetShortcut]\nURL=" + target);
            owner.Apps.Add(entry);
            return entry;
        }
    }

    public static async Task CheckMonitor()
    {
        var container = new ContainerModel { Id = "monitor-check" };
        AppHost.State.Document.Containers.Add(container);
        var target = Path.Combine(AppHost.DataRoot, "monitor-application.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "notepad.exe"), target, true);
        try
        {
            Require(AppHost.State.ImportFiles(container, [target]) == null && container.Apps.Count == 1, "monitor import");
            var app = container.Apps[0];
            container.Locked = true;
            File.Delete(target);
            AppHost.State.Reconcile(DateTime.UtcNow - AppState.MissingTargetGrace - TimeSpan.FromSeconds(1));
            AppHost.StartShortcutMonitor(TimeSpan.FromMilliseconds(50));
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (container.Apps.Count > 0 && DateTime.UtcNow < deadline)
                await Task.Delay(25);
            Require(container.Apps.Count == 0 && !File.Exists(AppHost.Shortcuts.PathOf(app)), "live timer cleanup");
            Require(!AppHost.Store.Load().Containers.First(item => item.Id == container.Id).Apps.Any(), "live cleanup persisted");
        }
        finally
        {
            AppHost.StartShortcutMonitor();
            AppHost.State.Document.Containers.Remove(container);
            AppHost.State.Flush();
        }
    }

    public static async Task CheckMenus(ContainerWindow window)
    {
        var icon = FindVisual<AppIconView>(window) ?? throw new InvalidOperationException("missing icon view");
        var card = FindMenuParent(icon) ?? throw new InvalidOperationException("missing container menu");
        var image = FindVisual<Image>(icon)!;
        var label = icon.Children.OfType<TextBlock>().First();
        foreach (var source in new FrameworkElement[] { image, label, icon })
        {
            // Use WPF's real menu owner selection and opening route without moving
            // the user's pointer. The first iteration fails with the old lazy menu.
            OpenMenu(source);
            try
            {
                Require(icon.ContextMenu?.IsOpen == true && !card.ContextMenu.IsOpen, "first icon context menu");
                Require(Headers(icon.ContextMenu!).Contains("打开") && Headers(icon.ContextMenu!).Contains("移出到桌面"), "icon actions");
            }
            finally
            {
                if (icon.ContextMenu != null) icon.ContextMenu.IsOpen = false;
                card.ContextMenu.IsOpen = false;
            }
            await Task.Delay(40);
        }
        var hit = icon.InputHitTest(new Point(2, 2));
        Require(hit != null, "icon padding hit test");
        window.Model.Locked = true;
        try
        {
            OpenMenu(image);
            Require(!Headers(icon.ContextMenu!).Contains("移出到桌面"), "locked menu refresh");
        }
        finally
        {
            icon.ContextMenu!.IsOpen = false;
            window.Model.Locked = false;
        }
        await Task.Delay(40);
        OpenMenu(card);
        try
        {
            Require(card.ContextMenu.IsOpen && Headers(card.ContextMenu).Contains("重命名"), "container background menu");
        }
        finally { card.ContextMenu.IsOpen = false; }
        await Task.Delay(40);
    }

    static void OpenMenu(IInputElement source)
    {
        // Reflection is restricted to this smoke check, never used in the app UI.
        var type = typeof(FrameworkElement).Assembly.GetType("System.Windows.Controls.PopupControlService")!;
        var service = type.GetProperty("Current", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
        var method = type.GetMethod("RaiseContextMenuOpeningEvent", BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(IInputElement), typeof(double), typeof(double), typeof(bool)], null)!;
        method.Invoke(service, [source, 2d, 2d, false]);
    }

    static string[] Headers(ContextMenu menu) => menu.Items.OfType<MenuItem>().Select(item => item.Header?.ToString() ?? "").ToArray();

    static FrameworkElement? FindMenuParent(DependencyObject child)
    {
        for (var parent = VisualTreeHelper.GetParent(child); parent != null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is FrameworkElement { ContextMenu: not null } element) return element;
        return null;
    }

    static T? FindVisual<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindVisual<T>(child) is { } nested) return nested;
        }
        return null;
    }

    static void Require(bool condition, string check)
    {
        if (!condition) throw new InvalidOperationException("shortcut check failed: " + check);
    }
}
