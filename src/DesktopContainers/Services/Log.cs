namespace DesktopContainers;

public static class Log
{
    static readonly object Gate = new();
    static string _path = "";

    public static void Init(string dataRoot)
    {
        var dir = Path.Combine(dataRoot, "logs");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "app.log");
        Info("start " + Environment.OSVersion.VersionString + " pid=" + Environment.ProcessId);
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? exception = null)
    {
        Write("ERROR", exception == null ? message : message + " " + exception);
    }

    static void Write(string level, string message)
    {
        try
        {
            if (string.IsNullOrEmpty(_path)) return;
            var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + level + " " + message + Environment.NewLine;
            lock (Gate) File.AppendAllText(_path, line);
        }
        catch
        {
            // 日志失败不能影响桌面
        }
    }
}
