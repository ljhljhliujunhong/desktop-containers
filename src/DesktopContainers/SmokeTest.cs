using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DesktopContainers;

public static class SmokeTest
{
    static DispatcherTimer? _timer;

    public static void Run()
    {
        var report = Path.Combine(AppHost.DataRoot, "smoke-report.txt");
        try
        {
            if (!AppHost.DataRoot.Contains("AgentCache", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("smoke 拒绝使用这个数据目录");

            if (Directory.Exists(AppHost.DataRoot))
                Directory.Delete(AppHost.DataRoot, true);
            Directory.CreateDirectory(AppHost.DataRoot);
            Log.Init(AppHost.DataRoot);

            AppHost.Store = new LayoutStore(AppHost.DataRoot);
            AppHost.State = AppState.Load(AppHost.Store);
            AppHost.Shortcuts = new ShortcutService(AppHost.DataRoot);
            AppHost.Icons = new IconService();
            DesktopPlacement.Install();

            var container = AppHost.State.CreateContainer("AI 应用", "strawberry-milk", false);
            var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
            var importError = AppHost.State.ImportFiles(container, [notepad]);
            AppHost.State.CreateContainer("开发工具", "sky-blue", false);

            var manager = new ManagerWindow();
            AppHost.Manager = manager;
            manager.Show();

            var onboarding = new NewContainerWindow(true);
            onboarding.Show();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _timer.Tick += (_, _) =>
            {
                _timer.Stop();
                File.WriteAllText(report, "timeout", new UTF8Encoding(false));
                Application.Current.Shutdown(1);
            };
            _timer.Start();

            Application.Current.Dispatcher.BeginInvoke(() => Finish(report, container, importError, manager, onboarding), DispatcherPriority.ApplicationIdle);
        }
        catch (Exception ex)
        {
            Log.Error("smoke", ex);
            File.WriteAllText(report, ex.ToString(), new UTF8Encoding(false));
            Application.Current.Shutdown(1);
        }
    }

    static void Finish(string report, ContainerModel container, string? importError, ManagerWindow manager, NewContainerWindow onboarding)
    {
        try
        {
            Directory.CreateDirectory(AppHost.DataRoot);
            Shot(manager, Path.Combine(AppHost.DataRoot, "manager-home.png"));
            Shot(onboarding, Path.Combine(AppHost.DataRoot, "onboarding.png"));
            onboarding.Close();
            manager.Navigate("themes");
            manager.UpdateLayout();
            Shot(manager, Path.Combine(AppHost.DataRoot, "manager-themes.png"));
            manager.Navigate("containers");
            manager.UpdateLayout();
            Shot(manager, Path.Combine(AppHost.DataRoot, "manager-containers.png"));
            manager.Navigate("settings");
            manager.UpdateLayout();
            Shot(manager, Path.Combine(AppHost.DataRoot, "manager-settings.png"));

            var window = AppHost.Windows.First(item => item.Model.Id == container.Id);
            window.UpdateLayout();
            Shot(window, Path.Combine(AppHost.DataRoot, "container.png"));
            window.ShowGlass(true);
            Shot(window, Path.Combine(AppHost.DataRoot, "container-glass.png"));
            window.ShowGlass(false);
            manager.Hide();

            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    CopyScreen(window, Path.Combine(AppHost.DataRoot, "container-screen.png"));
                    var hwnd = new WindowInteropHelper(window).Handle;
                    var above = DesktopPlacement.IsAboveDesktop(hwnd);
                    var entry = container.Apps.FirstOrDefault();
                    var iconOk = entry != null && AppHost.Icons.Extract(AppHost.Shortcuts.PathOf(entry), 64) != null;
                    var reloaded = new LayoutStore(AppHost.DataRoot).Load();
                    var notepadStillThere = File.Exists(Path.Combine(Environment.SystemDirectory, "notepad.exe"));
                    var stored = entry != null && File.Exists(AppHost.Shortcuts.PathOf(entry));
                    var text = string.Join(Environment.NewLine,
                    [
                        "above=" + above,
                        "icon=" + iconOk,
                        "stored=" + stored,
                        "notepad=" + notepadStillThere,
                        "import=" + (importError ?? "ok"),
                        "apps=" + container.Apps.Count,
                        "reloadContainers=" + reloaded.Containers.Count,
                        "reloadApps=" + reloaded.Containers.FirstOrDefault()?.Apps.Count,
                        "name=" + reloaded.Containers.FirstOrDefault()?.Name,
                        "z=" + DesktopPlacement.Describe(hwnd),
                        "os=" + Environment.OSVersion.VersionString,
                        "size=" + window.ActualWidth + "x" + window.ActualHeight
                    ]);
                    File.WriteAllText(report, text, new UTF8Encoding(false));
                    var ok = above && iconOk && stored && notepadStillThere && importError == null
                        && reloaded.Containers.Count == 2
                        && reloaded.Containers[0].Apps.Count == 1
                        && reloaded.Containers[0].Name == "AI 应用";
                    _timer?.Stop();
                    AppHost.IsExiting = true;
                    Application.Current.Shutdown(ok ? 0 : 2);
                }
                catch (Exception ex)
                {
                    Log.Error("smoke-finish", ex);
                    File.WriteAllText(report, ex.ToString(), new UTF8Encoding(false));
                    Application.Current.Shutdown(1);
                }
            }, DispatcherPriority.ApplicationIdle);
        }
        catch (Exception ex)
        {
            Log.Error("smoke-shot", ex);
            File.WriteAllText(report, ex.ToString(), new UTF8Encoding(false));
            Application.Current.Shutdown(1);
        }
    }

    static void Shot(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    static void CopyScreen(Window window, string path)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) return;
        using var bitmap = new System.Drawing.Bitmap(width, height);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new System.Drawing.Size(width, height));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}
