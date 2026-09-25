using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace DesktopContainers;

public static class AppHost
{
    static readonly List<ContainerWindow> _windows = new();

    public static LayoutStore Store { get; internal set; } = null!;
    public static AppState State { get; internal set; } = null!;
    public static ShortcutService Shortcuts { get; internal set; } = null!;
    public static IconService Icons { get; internal set; } = null!;
    public static TrayController? Tray { get; private set; }
    public static ManagerWindow? Manager { get; internal set; }
    public static string DataRoot { get; private set; } = "";
    public static bool IsExiting { get; internal set; }
    public static bool IsSmoke { get; private set; }
    public static bool SuppressPersist { get; set; }
    public static IReadOnlyList<ContainerWindow> Windows => _windows;

    public static void RepaintContainers()
    {
        foreach (var window in _windows.ToArray())
            window.Repaint();
    }

    public static void RefreshContainerLayout()
    {
        foreach (var window in _windows.ToArray())
            window.RefreshLayout();
    }

    public static bool ShowsTitle(ContainerModel model)
    {
        if (State.Settings.UnifyTitles) return State.Settings.UnifiedShowTitles;
        return model.ShowTitle && model.TitlePlacement != TitlePlacement.Hidden;
    }

    public static bool ShowsNames(ContainerModel model)
    {
        if (State.Settings.UnifyNames) return State.Settings.UnifiedShowNames;
        return model.ShowNames;
    }

    public static double IconSize(ContainerModel model)
    {
        if (State.Settings.UnifyIconSize)
            return State.Settings.UnifiedIconSize;
        return model.IconSize;
    }

    public static void Start(string[] args)
    {
        var export = Array.IndexOf(args, "--export-icon");
        if (export >= 0)
        {
            var path = export + 1 < args.Length
                ? args[export + 1]
                : Path.Combine(AppContext.BaseDirectory, "app.ico");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            BrandIcon.SaveIco(path);
            BrandIcon.SaveCanonical();
            Application.Current.Shutdown(0);
            return;
        }

        IsSmoke = args.Contains("--smoke");
        DataRoot = ResolveDataRoot(args);
        Directory.CreateDirectory(DataRoot);
        Log.Init(DataRoot);
        Log.Info("os=" + Environment.OSVersion.VersionString + " args=" + string.Join(" ", args));

        if (IsSmoke)
        {
            SmokeTest.Run();
            return;
        }

        if (!SingleInstance.Acquire(DataRoot, () => ShowManager()))
        {
            Application.Current.Shutdown();
            return;
        }

        Store = new LayoutStore(DataRoot);
        Store.Snapshot("session");
        State = AppState.Load(Store);
        Shortcuts = new ShortcutService(DataRoot);
        Icons = new IconService();
        State.Reconcile();
        _ = StartupService.Apply(State.Settings.LaunchAtStartup);
        DesktopPlacement.Install();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettings;
        SystemEvents.SessionEnding += OnSessionEnding;

        foreach (var container in State.Document.Containers.ToList())
            OpenContainer(container, false);

        Tray = new TrayController();
        BrandIcon.SaveCanonical();

        if (args.Contains("--startup"))
            return;

        if (!State.Settings.CompletedOnboarding)
        {
            var dialog = new NewContainerWindow(true);
            dialog.ShowDialog();
            State.Settings.CompletedOnboarding = true;
            State.Flush();
        }

        ShowManager();
    }

    public static ContainerWindow OpenContainer(ContainerModel model, bool placeOnCursor)
    {
        var index = State.Document.Containers.IndexOf(model);
        var window = new ContainerWindow(model, placeOnCursor, Math.Max(0, index));
        _windows.Add(window);
        window.Closed += (_, _) => _windows.Remove(window);
        window.Show();
        return window;
    }

    public static void CloseContainerWindow(string id)
    {
        FindWindow(id)?.Close();
    }

    public static ContainerWindow? FindWindow(string id) =>
        _windows.FirstOrDefault(window => window.Model.Id == id);

    public static void ShowManager(string? page = null, string? containerId = null)
    {
        if (Manager == null)
        {
            Manager = new ManagerWindow();
            Manager.Show();
        }

        if (page != null || containerId != null)
        {
            Manager.Open(page ?? "home", containerId);
            return;
        }

        if (!Manager.IsVisible) Manager.Show();
        if (Manager.WindowState == WindowState.Minimized)
            Manager.WindowState = WindowState.Normal;
        Manager.Activate();
    }

    public static void SetEditMode(bool enabled)
    {
        if (State.Settings.EditMode != enabled)
        {
            State.Settings.EditMode = enabled;
            State.Flush();
        }
        foreach (var window in _windows)
            window.ApplyChrome();
        Manager?.SyncEditSwitch();
        Tray?.Sync();
    }

    public static void Exit()
    {
        if (IsExiting) return;
        IsExiting = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettings;
        SystemEvents.SessionEnding -= OnSessionEnding;
        try { State?.Flush(); } catch (Exception ex) { Log.Error("flush", ex); }
        Tray?.Dispose();
        Tray = null;
        foreach (var window in _windows.ToList())
            window.Close();
        Manager?.Close();
        Application.Current?.Shutdown();
    }

    static void OnDisplaySettings(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            foreach (var window in _windows.ToList())
                window.ClampAndSave();
        });
    }

    static void OnSessionEnding(object sender, SessionEndingEventArgs e)
    {
        try
        {
            Application.Current?.Dispatcher.Invoke(() => State?.Flush(), TimeSpan.FromSeconds(2));
        }
        catch
        {
            try { State?.Flush(); } catch { /* 关机时能写多少写多少 */ }
        }
    }

    static string ResolveDataRoot(string[] args)
    {
        var index = Array.IndexOf(args, "--data");
        if (index >= 0 && index + 1 < args.Length)
            return Path.GetFullPath(args[index + 1]);
        var env = Environment.GetEnvironmentVariable("DESKTOP_CONTAINERS_HOME");
        if (!string.IsNullOrWhiteSpace(env))
            return Path.GetFullPath(env);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopContainers");
    }
}
