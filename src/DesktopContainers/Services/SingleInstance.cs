using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace DesktopContainers;

public static class SingleInstance
{
    static Mutex? _mutex;
    static EventWaitHandle? _event;
    static Thread? _thread;

    public static bool Acquire(string dataRoot, Action onSignal)
    {
        var name = "Local\\DesktopContainers_" + Hash(dataRoot);
        _mutex = new Mutex(false, name + "_m");
        try
        {
            if (!_mutex.WaitOne(0))
            {
                Signal(name);
                return false;
            }
        }
        catch (AbandonedMutexException)
        {
            // 上次崩溃留下的互斥量，这次可以继续启动
        }

        _event = new EventWaitHandle(false, EventResetMode.AutoReset, name + "_e");
        _thread = new Thread(() =>
        {
            while (_event.WaitOne())
                Application.Current?.Dispatcher.BeginInvoke(onSignal);
        })
        {
            IsBackground = true,
            Name = "desktop-containers-instance"
        };
        _thread.Start();
        return true;
    }

    static void Signal(string name)
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(name + "_e");
            ev.Set();
        }
        catch (Exception ex)
        {
            Log.Error("signal", ex);
        }
    }

    static string Hash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..16];
    }
}
