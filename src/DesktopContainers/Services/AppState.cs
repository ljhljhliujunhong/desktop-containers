using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace DesktopContainers;

public sealed class AppState
{
    readonly LayoutStore _store;
    DispatcherTimer? _saveTimer;

    public AppState(LayoutStore store, LayoutDocument document)
    {
        _store = store;
        Document = document;
        Normalize(Document);
    }

    public LayoutDocument Document { get; private set; }
    public AppSettings Settings => Document.Settings;

    public static AppState Load(LayoutStore store) => new(store, store.Load());

    public ContainerModel? Find(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : Document.Containers.FirstOrDefault(container => container.Id == id);

    public void Reconcile()
    {
        var changed = false;
        foreach (var container in Document.Containers)
        {
            foreach (var app in container.Apps)
            {
                var missing = ShortcutService.IsMissingTarget(app.TargetPath, AppHost.Shortcuts.PathOf(app));
                if (app.Missing == missing) continue;
                app.Missing = missing;
                changed = true;
            }
        }
        if (changed) Flush();
    }

    public ContainerModel CreateContainer(string name, string? themeId, bool placeOnCursor) =>
        CreateContainer(name, themeId, null, placeOnCursor);

    public ContainerModel CreateContainer(string name, string? themeId, Appearance? appearance, bool placeOnCursor)
    {
        var theme = ThemeCatalog.Find(themeId, Document.CustomThemes)
            ?? ThemeCatalog.Find(Settings.DefaultThemeId, Document.CustomThemes)
            ?? ThemeCatalog.BuiltIns[0];
        var index = Document.Containers.Count;
        var trimmed = (string.IsNullOrWhiteSpace(name) ? "新容器" : name.Trim());
        if (trimmed.Length > 32) trimmed = trimmed[..32];
        var model = new ContainerModel
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = trimmed,
            X = 40 + (index % 3) * 468,
            Y = 36 + ((index / 3) % 3) * 344,
            Width = 440,
            Height = 320,
            ShowTitle = Settings.ShowTitlesByDefault,
            ShowNames = true,
            IconSize = Math.Clamp(Settings.DefaultIconSize, 32, 96),
            TitlePlacement = TitlePlacement.Top,
            ThemeId = theme.Id,
            Appearance = appearance?.Clone() ?? ThemeAppearance(theme.Id).Clone(),
            UpdatedUtc = DateTime.UtcNow
        };
        Document.Containers.Add(model);
        AppHost.OpenContainer(model, placeOnCursor);
        Flush();
        return model;
    }

    public string? DeleteContainer(ContainerModel model)
    {
        var restored = new List<(AppEntry entry, string desktopPath)>();
        foreach (var app in model.Apps.ToList())
        {
            if (app.Missing || !File.Exists(AppHost.Shortcuts.PathOf(app)))
                continue;
            if (!AppHost.Shortcuts.RestoreToDesktop(app, out var dest) || dest == null)
            {
                Rollback(restored);
                return "没能把快捷方式移回桌面";
            }
            restored.Add((app, dest));
        }

        foreach (var app in model.Apps.ToList())
            AppHost.Shortcuts.DeleteOwned(app);

        Document.Containers.Remove(model);
        AppHost.CloseContainerWindow(model.Id);
        Flush();
        return null;
    }

    public void Rename(ContainerModel container, string name)
    {
        name = name.Trim();
        if (name.Length == 0) return;
        if (name.Length > 32) name = name[..32];
        container.Name = name;
        container.UpdatedUtc = DateTime.UtcNow;
        Flush();
    }

    public string? ImportFiles(ContainerModel container, IEnumerable<string> files)
    {
        if (container.Locked) return "这个容器锁着";
        var added = 0;
        string? error = null;
        var rejected = 0;
        foreach (var file in files.Take(300))
        {
            var result = AppHost.Shortcuts.Import(file);
            if (result.Entry == null)
            {
                if (result.Error is "skip-type" or "skip-self")
                {
                    rejected++;
                    continue;
                }
                error ??= result.Error;
                continue;
            }

            var entry = result.Entry;
            var owner = Document.Containers.FirstOrDefault(item =>
                item.Apps.Any(app => app.Key.Length > 0 && app.Key == entry.Key));
            if (owner != null)
            {
                AppHost.Shortcuts.DeleteOwned(entry);
                var existing = owner.Apps.First(app => app.Key == entry.Key);
                if (owner != container && !owner.Locked && !container.Locked)
                {
                    owner.Apps.Remove(existing);
                    container.Apps.Add(existing);
                    added++;
                }
                if (result.DesktopShortcut)
                    AppHost.Shortcuts.TryDeleteDesktopSource(result.SourcePath);
                continue;
            }

            container.Apps.Add(entry);
            if (result.DesktopShortcut)
                AppHost.Shortcuts.TryDeleteDesktopSource(result.SourcePath);
            added++;
        }

        if (added > 0)
        {
            container.UpdatedUtc = DateTime.UtcNow;
            Flush();
            return null;
        }
        if (error != null) return error;
        return rejected > 0 ? "这些文件不能放进容器" : null;
    }

    public bool MoveApp(string fromId, string appId, string toId, int index)
    {
        var from = Find(fromId);
        var to = Find(toId);
        if (from == null || to == null || from.Locked || to.Locked) return false;
        var app = from.Apps.FirstOrDefault(item => item.Id == appId);
        if (app == null) return false;
        var oldIndex = from.Apps.IndexOf(app);
        from.Apps.RemoveAt(oldIndex);
        if (ReferenceEquals(from, to) && index > oldIndex) index--;
        if (index < 0 || index > to.Apps.Count) index = to.Apps.Count;
        to.Apps.Insert(index, app);
        var now = DateTime.UtcNow;
        from.UpdatedUtc = now;
        to.UpdatedUtc = now;
        Flush();
        return true;
    }

    public bool ShiftApp(ContainerModel container, AppEntry app, int delta)
    {
        var index = container.Apps.IndexOf(app);
        if (index < 0 || container.Locked) return false;
        var target = index + delta;
        if (target < 0 || target >= container.Apps.Count) return false;
        var insert = delta > 0 ? target + 1 : target;
        return MoveApp(container.Id, app.Id, container.Id, insert);
    }

    public bool RestoreApp(ContainerModel container, AppEntry app)
    {
        if (container.Locked) return false;
        if (!AppHost.Shortcuts.RestoreToDesktop(app, out _)) return false;
        container.Apps.Remove(app);
        container.UpdatedUtc = DateTime.UtcNow;
        Flush();
        return true;
    }

    public void RemoveMissing(ContainerModel container, AppEntry app)
    {
        container.Apps.Remove(app);
        AppHost.Shortcuts.DeleteOwned(app);
        container.UpdatedUtc = DateTime.UtcNow;
        Flush();
    }

    public void SortApps(ContainerModel container)
    {
        if (container.Locked) return;
        var ordered = container.Apps
            .OrderBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        container.Apps.Clear();
        foreach (var app in ordered) container.Apps.Add(app);
        container.UpdatedUtc = DateTime.UtcNow;
        Flush();
    }

    public void MoveContainer(ContainerModel container, int delta)
    {
        var index = Document.Containers.IndexOf(container);
        var next = index + delta;
        if (index < 0 || next < 0 || next >= Document.Containers.Count) return;
        Document.Containers.Move(index, next);
        Flush();
    }

    public void PreviewAppearance(IEnumerable<string> containerIds, Appearance appearance)
    {
        foreach (var id in containerIds)
        {
            var container = Find(id);
            if (container == null) continue;
            container.Appearance.CopyFrom(appearance);
            container.UpdatedUtc = DateTime.UtcNow;
        }
        RequestSave();
    }

    public void ApplyAppearance(IEnumerable<string> containerIds, Appearance appearance, string? themeId)
    {
        foreach (var id in containerIds)
        {
            var container = Find(id);
            if (container == null) continue;
            container.Appearance.CopyFrom(appearance);
            if (!string.IsNullOrWhiteSpace(themeId))
                container.ThemeId = themeId;
            container.UpdatedUtc = DateTime.UtcNow;
        }
        Flush();
    }

    public ThemeDefinition SaveCustomTheme(string name, Appearance appearance)
    {
        var theme = new ThemeDefinition
        {
            Id = "custom-" + Guid.NewGuid().ToString("N")[..8],
            Name = string.IsNullOrWhiteSpace(name) ? "我的主题" : name.Trim(),
            BuiltIn = false,
            Appearance = appearance.Clone()
        };
        Document.CustomThemes.Add(theme);
        Flush();
        return theme;
    }

    public void UpdateCustomTheme(string id, Appearance appearance)
    {
        var theme = Document.CustomThemes.FirstOrDefault(item => item.Id == id);
        if (theme == null) return;
        theme.Appearance.CopyFrom(appearance);
        Flush();
    }

    public void DeleteCustomTheme(string id)
    {
        var theme = Document.CustomThemes.FirstOrDefault(item => item.Id == id);
        if (theme == null) return;
        Document.CustomThemes.Remove(theme);
        if (Settings.DefaultThemeId == id)
            Settings.DefaultThemeId = ThemeCatalog.DefaultId;
        Flush();
    }

    public Appearance ThemeAppearance(string? id)
    {
        var theme = ThemeCatalog.Find(id, Document.CustomThemes)
            ?? ThemeCatalog.Find(Settings.DefaultThemeId, Document.CustomThemes)
            ?? ThemeCatalog.BuiltIns[0];
        Settings.ThemeEdits ??= new List<ThemeEdit>();
        var edit = Settings.ThemeEdits.FirstOrDefault(item => item.Id == theme.Id);
        return edit?.Appearance ?? theme.Appearance;
    }

    public void SaveThemeEdit(string id, Appearance appearance)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        Settings.ThemeEdits ??= new List<ThemeEdit>();
        var edit = Settings.ThemeEdits.FirstOrDefault(item => item.Id == id);
        if (edit == null)
        {
            edit = new ThemeEdit { Id = id };
            Settings.ThemeEdits.Add(edit);
        }
        edit.Appearance = appearance.Clone();
        RequestSave();
    }

    public void SetDefaultTheme(string id)
    {
        Settings.DefaultThemeId = id;
        Flush();
    }

    public bool SetLaunchAtStartup(bool enabled)
    {
        if (!StartupService.Apply(enabled)) return false;
        Settings.LaunchAtStartup = enabled;
        Flush();
        return true;
    }

    public string? RestoreFrom(string path)
    {
        LayoutDocument incoming;
        try
        {
            incoming = _store.Read(path);
        }
        catch (Exception ex)
        {
            Log.Error("restore", ex);
            return "这个文件读不了";
        }

        _store.Snapshot("before-restore");
        Normalize(incoming);
        AppHost.SuppressPersist = true;
        foreach (var window in AppHost.Windows.ToList())
            window.Close();
        AppHost.SuppressPersist = false;
        Document = incoming;
        _store.Save(Document);
        StartupService.Apply(Settings.LaunchAtStartup);
        foreach (var container in Document.Containers)
            AppHost.OpenContainer(container, false);
        AppHost.SetEditMode(Settings.EditMode);
        return null;
    }

    public void RequestSave()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            Flush();
            return;
        }
        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(RequestSave);
            return;
        }
        _saveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Stop();
        _saveTimer.Tick -= OnSaveTick;
        _saveTimer.Tick += OnSaveTick;
        _saveTimer.Start();
    }

    public void Flush()
    {
        _saveTimer?.Stop();
        _store.Save(Document);
    }

    void OnSaveTick(object? sender, EventArgs e) => Flush();

    void Rollback(List<(AppEntry entry, string desktopPath)> restored)
    {
        foreach (var (entry, path) in restored)
        {
            try
            {
                if (File.Exists(path))
                    File.Move(path, AppHost.Shortcuts.PathOf(entry));
            }
            catch (Exception ex)
            {
                Log.Error("rollback", ex);
            }
        }
    }

    static void Normalize(LayoutDocument document)
    {
        document.Settings ??= new AppSettings();
        document.CustomThemes ??= new ObservableCollection<ThemeDefinition>();
        document.Containers ??= new ObservableCollection<ContainerModel>();
        if (document.Version <= 0) document.Version = 1;
        if (document.Settings.DefaultIconSize is < 32 or > 96)
            document.Settings.DefaultIconSize = 56;
        if (string.IsNullOrWhiteSpace(document.Settings.DefaultThemeId))
            document.Settings.DefaultThemeId = ThemeCatalog.DefaultId;
        document.Settings.ThemeEdits ??= new List<ThemeEdit>();
        foreach (var edit in document.Settings.ThemeEdits)
        {
            if (string.IsNullOrWhiteSpace(edit.Id))
                edit.Id = ThemeCatalog.DefaultId;
            edit.Appearance ??= ThemeCatalog.CreateAppearance(edit.Id);
        }

        foreach (var theme in document.CustomThemes)
        {
            theme.BuiltIn = false;
            if (string.IsNullOrWhiteSpace(theme.Id))
                theme.Id = "custom-" + Guid.NewGuid().ToString("N")[..8];
            if (string.IsNullOrWhiteSpace(theme.Name))
                theme.Name = "我的主题";
            theme.Appearance ??= ThemeCatalog.CreateAppearance(ThemeCatalog.DefaultId);
        }

        foreach (var container in document.Containers)
        {
            if (string.IsNullOrWhiteSpace(container.Id))
                container.Id = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(container.Name))
                container.Name = "新容器";
            container.Apps ??= new ObservableCollection<AppEntry>();
            container.Appearance ??= ThemeCatalog.CreateAppearance(container.ThemeId, document.CustomThemes);
            if (container.Width < 96) container.Width = 440;
            if (container.Height < 96) container.Height = 320;
            if (container.IconSize is < 32 or > 96) container.IconSize = 56;
            foreach (var app in container.Apps)
            {
                if (string.IsNullOrWhiteSpace(app.Id))
                    app.Id = Guid.NewGuid().ToString("N");
                if (string.IsNullOrWhiteSpace(app.DisplayName))
                    app.DisplayName = "应用";
                if (string.IsNullOrWhiteSpace(app.FileName))
                    app.FileName = app.Id + ".lnk";
            }
        }
    }
}
