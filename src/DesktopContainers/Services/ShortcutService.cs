namespace DesktopContainers;

public sealed class ImportOne
{
    public AppEntry? Entry { get; init; }
    public string? Error { get; init; }
    public string? SourcePath { get; init; }
    public bool DesktopShortcut { get; init; }

    public static ImportOne Ok(AppEntry entry, string source, bool desktop) =>
        new() { Entry = entry, SourcePath = source, DesktopShortcut = desktop };

    public static ImportOne Fail(string error) => new() { Error = error };
}

public sealed class ShortcutService
{
    public string StoreDir { get; }

    public ShortcutService(string dataRoot)
    {
        StoreDir = Path.Combine(dataRoot, "shortcuts");
        Directory.CreateDirectory(StoreDir);
    }

    public string PathOf(AppEntry entry) => Path.Combine(StoreDir, entry.FileName);

    public static bool IsMissingTarget(string target, string storedPath)
    {
        if (!File.Exists(storedPath)) return true;
        if (string.IsNullOrWhiteSpace(target)) return false;
        if (target.Contains("://", StringComparison.Ordinal)) return false;
        if (File.Exists(target) || Directory.Exists(target)) return false;
        return target.Contains('\\') || target.Contains('/');
    }

    public ImportOne Import(string sourcePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                return ImportOne.Fail("找不到文件");

            var full = Path.GetFullPath(sourcePath);
            var storeRoot = Path.GetFullPath(StoreDir) + Path.DirectorySeparatorChar;
            if (full.StartsWith(storeRoot, StringComparison.OrdinalIgnoreCase))
                return ImportOne.Fail("skip-self");

            var ext = Path.GetExtension(full).ToLowerInvariant();
            if (ext is not ".lnk" and not ".url" and not ".exe")
                return ImportOne.Fail("skip-type");

            var id = Guid.NewGuid().ToString("N");
            var destExt = ext == ".exe" ? ".lnk" : ext;
            var dest = Path.Combine(StoreDir, id + destExt);
            if (ext == ".exe")
                CreateExeShortcut(dest, full);
            else
                Retry(() => File.Copy(full, dest, false));

            var display = Path.GetFileNameWithoutExtension(full);
            if (string.IsNullOrWhiteSpace(display)) display = "应用";
            var (target, args) = ext == ".exe" ? (full, "") : ReadLink(dest);
            if (ext == ".url" && string.IsNullOrWhiteSpace(target))
                target = ReadUrl(dest);

            var entry = new AppEntry
            {
                Id = id,
                FileName = Path.GetFileName(dest),
                DisplayName = display,
                TargetPath = target,
                Key = MakeKey(target, args, display),
                Missing = IsMissingTarget(target, dest)
            };
            var desktop = (ext is ".lnk" or ".url") && IsUnderDesktop(full);
            return ImportOne.Ok(entry, full, desktop);
        }
        catch (Exception ex)
        {
            Log.Error("import", ex);
            return ImportOne.Fail("没能放进来");
        }
    }

    public bool TryDeleteDesktopSource(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return true;
        if (!IsUnderDesktop(sourcePath)) return false;
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (ext is not ".lnk" and not ".url") return false;
        try
        {
            Retry(() => File.Delete(sourcePath));
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("remove desktop shortcut", ex);
            return false;
        }
    }

    public bool RestoreToDesktop(AppEntry entry, out string? desktopPath)
    {
        desktopPath = null;
        try
        {
            var src = PathOf(entry);
            if (!Owned(src) || !File.Exists(src)) return false;
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            Directory.CreateDirectory(desktop);
            var name = Sanitize(entry.DisplayName) + Path.GetExtension(src);
            var dest = Unique(desktop, name);
            Retry(() => File.Move(src, dest));
            desktopPath = dest;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("restore", ex);
            return false;
        }
    }

    public void DeleteOwned(AppEntry entry)
    {
        var path = PathOf(entry);
        if (!Owned(path) || !File.Exists(path)) return;
        try { File.Delete(path); }
        catch (Exception ex) { Log.Error("delete shortcut", ex); }
    }

    public bool TryLaunch(AppEntry entry)
    {
        var path = PathOf(entry);
        entry.Missing = IsMissingTarget(entry.TargetPath, path);
        if (entry.Missing) return false;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("launch", ex);
            entry.Missing = true;
            return false;
        }
    }

    public void Reveal(AppEntry entry)
    {
        var target = entry.TargetPath;
        if (string.IsNullOrWhiteSpace(target)) return;
        if (!File.Exists(target) && !Directory.Exists(target)) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "/select,\"" + target + "\"",
            UseShellExecute = true
        });
    }

    bool Owned(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(StoreDir) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    bool IsUnderDesktop(string path)
    {
        var full = Path.GetFullPath(path);
        return DesktopRoots().Any(root => full.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }

    static IEnumerable<string> DesktopRoots()
    {
        yield return WithSlash(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        if (!string.IsNullOrWhiteSpace(common))
            yield return WithSlash(common);
    }

    static string WithSlash(string path) => Path.GetFullPath(path).TrimEnd('\\') + "\\";

    static void CreateExeShortcut(string dest, string exePath)
    {
        dynamic shell = CreateShell();
        dynamic shortcut = shell.CreateShortcut(dest);
        shortcut.TargetPath = exePath;
        shortcut.WorkingDirectory = Path.GetDirectoryName(exePath) ?? "";
        shortcut.Save();
    }

    public static (string file, int index) IconSource(string path)
    {
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".lnk")
            {
                dynamic shell = CreateShell();
                dynamic shortcut = shell.CreateShortcut(path);
                string icon = "";
                try { icon = shortcut.IconLocation ?? ""; } catch { /* 没有自定义图标 */ }
                if (TrySplitIcon(icon, out var file, out var index) && File.Exists(file))
                    return (file, index);
                string target = shortcut.TargetPath ?? "";
                if (!string.IsNullOrWhiteSpace(target) && File.Exists(target))
                    return (target, 0);
            }
            else if (ext == ".url")
            {
                string? file = null;
                var index = 0;
                foreach (var line in File.ReadLines(path))
                {
                    if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
                        file = line["IconFile=".Length..].Trim().Trim('"');
                    else if (line.StartsWith("IconIndex=", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(line["IconIndex=".Length..].Trim(), out var parsed))
                        index = parsed;
                }
                if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
                    return (file, index);
            }
        }
        catch (Exception ex)
        {
            Log.Error("icon source", ex);
        }
        return (path, 0);
    }

    static bool TrySplitIcon(string icon, out string file, out int index)
    {
        file = "";
        index = 0;
        if (string.IsNullOrWhiteSpace(icon)) return false;
        var comma = icon.LastIndexOf(',');
        if (comma > 1 && int.TryParse(icon[(comma + 1)..], out index))
        {
            file = icon[..comma].Trim().Trim('"');
            return file.Length > 0;
        }
        file = icon.Trim().Trim('"');
        return file.Length > 0;
    }

    static (string target, string args) ReadLink(string path)
    {
        try
        {
            dynamic shell = CreateShell();
            dynamic shortcut = shell.CreateShortcut(path);
            string target = shortcut.TargetPath ?? "";
            string args = "";
            try { args = shortcut.Arguments ?? ""; } catch { /* .url 没有参数 */ }
            return (target ?? "", args ?? "");
        }
        catch (Exception ex)
        {
            Log.Error("read link", ex);
            return ("", "");
        }
    }

    static string ReadUrl(string path)
    {
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                    return line[4..].Trim();
            }
        }
        catch (Exception ex)
        {
            Log.Error("read url", ex);
        }
        return "";
    }

    static dynamic CreateShell()
    {
        var type = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell 不可用");
        return Activator.CreateInstance(type) ?? throw new InvalidOperationException("WScript.Shell 创建失败");
    }

    static string MakeKey(string target, string args, string displayName)
    {
        if (!string.IsNullOrWhiteSpace(target))
            return (target + "|" + args).ToLowerInvariant();
        return "name:" + displayName.ToLowerInvariant();
    }

    public static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var text = new string(chars).Trim().TrimEnd('.');
        if (text.Length == 0) text = "应用";
        if (text.Length > 80) text = text[..80];
        return text;
    }

    static string Unique(string dir, string fileName)
    {
        var path = Path.Combine(dir, fileName);
        if (!File.Exists(path)) return path;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 2; i < 100; i++)
        {
            path = Path.Combine(dir, stem + " (" + i + ")" + ext);
            if (!File.Exists(path)) return path;
        }
        return Path.Combine(dir, Guid.NewGuid().ToString("N") + ext);
    }

    static void Retry(Action action)
    {
        for (var i = 0; i < 5; i++)
        {
            try
            {
                action();
                return;
            }
            catch (IOException) when (i < 4)
            {
                Thread.Sleep(40);
            }
            catch (UnauthorizedAccessException) when (i < 4)
            {
                Thread.Sleep(40);
            }
        }
    }
}
