using System.Text.Json;

namespace DesktopContainers;

public sealed class LayoutStore
{
    public string Root { get; }
    public string LayoutPath { get; }
    public string BackupPath { get; }
    public string BackupDir { get; }

    public LayoutStore(string root)
    {
        Root = root;
        LayoutPath = Path.Combine(root, "layout.json");
        BackupPath = Path.Combine(root, "layout.json.bak");
        BackupDir = Path.Combine(root, "backups");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(BackupDir);
        Directory.CreateDirectory(Path.Combine(root, "shortcuts"));
    }

    public LayoutDocument Load()
    {
        if (!File.Exists(LayoutPath))
            return new LayoutDocument();
        try
        {
            return Read(LayoutPath);
        }
        catch (Exception ex)
        {
            Log.Error("layout", ex);
            try
            {
                if (File.Exists(BackupPath))
                    return Read(BackupPath);
            }
            catch (Exception bakEx)
            {
                Log.Error("layout.bak", bakEx);
            }

            var newest = Directory.GetFiles(BackupDir, "layout-*.json")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (newest != null)
            {
                try { return Read(newest); }
                catch (Exception snapEx) { Log.Error("layout snapshot", snapEx); }
            }

            try
            {
                File.Copy(LayoutPath, LayoutPath + ".bad", true);
            }
            catch (Exception copyEx)
            {
                Log.Error("layout.bad", copyEx);
            }
            return new LayoutDocument();
        }
    }

    public LayoutDocument Read(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<LayoutDocument>(json, JsonOpts.Options) ?? new LayoutDocument();
    }

    public void Save(LayoutDocument document)
    {
        var tmp = LayoutPath + ".tmp";
        var json = JsonSerializer.Serialize(document, JsonOpts.Options);
        File.WriteAllText(tmp, json);
        if (File.Exists(LayoutPath))
            File.Replace(tmp, LayoutPath, BackupPath, true);
        else
            File.Move(tmp, LayoutPath);
    }

    public void Snapshot(string reason)
    {
        if (!File.Exists(LayoutPath)) return;
        var safe = string.Concat(reason.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '-' : ch));
        var name = "layout-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + safe + ".json";
        File.Copy(LayoutPath, Path.Combine(BackupDir, name), true);
        var old = Directory.GetFiles(BackupDir, "layout-*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Skip(8)
            .ToList();
        foreach (var file in old)
        {
            try { File.Delete(file); } catch { /* 旧备份删不掉也不影响这次保存 */ }
        }
    }
}
