using System.Text.Json;
using System.IO.Compression;

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
        var candidates = new List<string>();
        if (File.Exists(LayoutPath)) candidates.Add(LayoutPath);
        if (File.Exists(BackupPath)) candidates.Add(BackupPath);
        candidates.AddRange(Directory.GetFiles(BackupDir, "layout-*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc));
        foreach (var candidate in candidates)
        {
            try
            {
                return Read(candidate);
            }
            catch (Exception ex)
            {
                Log.Error("layout " + candidate, ex);
            }
        }

        if (File.Exists(LayoutPath))
        {
            try
            {
                File.Copy(LayoutPath, LayoutPath + ".bad", true);
            }
            catch (Exception copyEx)
            {
                Log.Error("layout.bad", copyEx);
            }
        }
        return new LayoutDocument();
    }

    public LayoutDocument Read(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<LayoutDocument>(json, JsonOpts.Options)
            ?? throw new InvalidDataException("布局文件为空");
    }

    public void ExportBundle(string path, LayoutDocument document)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            var layout = archive.CreateEntry("layout.json", CompressionLevel.Optimal);
            using (var writer = new StreamWriter(layout.Open()))
                writer.Write(JsonSerializer.Serialize(document, JsonOpts.Options));

            foreach (var name in document.Containers.SelectMany(c => c.Apps).Select(a => a.FileName)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!SafeShortcutName(name))
                    throw new InvalidDataException("布局里有无效的快捷方式文件名");
                var source = Path.Combine(Root, "shortcuts", name);
                if (!File.Exists(source))
                    throw new FileNotFoundException("备份缺少快捷方式", source);
                archive.CreateEntryFromFile(source, "shortcuts/" + name, CompressionLevel.Optimal);
            }
        }
        File.WriteAllBytes(path, memory.ToArray());
    }

    public static bool SafeShortcutName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name == Path.GetFileName(name)
        && Path.GetExtension(name).ToLowerInvariant() is ".lnk" or ".url";

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
