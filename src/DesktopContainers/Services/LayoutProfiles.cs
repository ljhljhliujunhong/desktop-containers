namespace DesktopContainers;

public static class LayoutProfiles
{
    public static LayoutProfile Save(string name, string? id = null)
    {
        var document = AppHost.State.Document;
        name = ValidateName(name, id);
        var snapshot = DisplayMemory.CaptureCurrent();
        if (snapshot.Spots.Count == 0) throw new InvalidOperationException("还没有可保存的容器。");
        var profile = document.LayoutProfiles.FirstOrDefault(item => item.Id == id);
        if (id != null && profile == null) throw new InvalidOperationException("这个布局已不存在。");
        var isNew = profile == null;
        profile ??= new LayoutProfile { Id = Guid.NewGuid().ToString("N"), Name = name };
        var oldScreens = profile.Screens;
        var oldSaved = profile.SavedUtc;
        var oldActive = document.ActiveLayoutId;
        var oldStartup = document.StartupLayoutId;
        profile.Screens = oldScreens.Where(screen => screen.Key != snapshot.Key).Select(LayoutProjection.Clone).ToList();
        profile.Screens.Add(LayoutProjection.Clone(snapshot));
        profile.SavedUtc = DateTime.UtcNow;
        if (isNew) document.LayoutProfiles.Add(profile);
        document.ActiveLayoutId = profile.Id;
        if (isNew && document.LayoutProfiles.Count == 1) document.StartupLayoutId = profile.Id;
        try { AppHost.State.Flush(); }
        catch
        {
            if (isNew) document.LayoutProfiles.Remove(profile);
            profile.Screens = oldScreens;
            profile.SavedUtc = oldSaved;
            document.ActiveLayoutId = oldActive;
            document.StartupLayoutId = oldStartup;
            throw;
        }
        return profile;
    }

    public static void Rename(LayoutProfile profile, string name)
    {
        name = ValidateName(name, profile.Id);
        var old = profile.Name;
        profile.Name = name;
        try { AppHost.State.Flush(); }
        catch { profile.Name = old; throw; }
    }

    public static void Delete(LayoutProfile profile)
    {
        var document = AppHost.State.Document;
        var index = document.LayoutProfiles.IndexOf(profile);
        if (index < 0) return;
        var active = document.ActiveLayoutId;
        var startup = document.StartupLayoutId;
        document.LayoutProfiles.RemoveAt(index);
        if (active == profile.Id) document.ActiveLayoutId = null;
        if (startup == profile.Id) document.StartupLayoutId = null;
        try { AppHost.State.Flush(); }
        catch
        {
            document.LayoutProfiles.Insert(index, profile);
            document.ActiveLayoutId = active;
            document.StartupLayoutId = startup;
            throw;
        }
    }

    public static void SetStartup(string? id)
    {
        var document = AppHost.State.Document;
        if (id != null && !document.LayoutProfiles.Any(profile => profile.Id == id))
            throw new InvalidOperationException("这个布局已不存在。");
        var old = document.StartupLayoutId;
        document.StartupLayoutId = id;
        try { AppHost.State.Flush(); }
        catch { document.StartupLayoutId = old; throw; }
    }

    static string ValidateName(string name, string? id)
    {
        name = name.Trim();
        if (name.Length == 0) throw new InvalidOperationException("请填写布局名称。");
        if (name.Length > 32) throw new InvalidOperationException("布局名称最多 32 个字。");
        if (AppHost.State.Document.LayoutProfiles.Any(profile => profile.Id != id
            && string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("已有同名布局，请换一个名称。");
        return name;
    }
}
