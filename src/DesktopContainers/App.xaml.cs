using System.Windows;
using System.Windows.Threading;

namespace DesktopContainers;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("unhandled", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("task", args.Exception);
            args.SetObserved();
        };

        try
        {
            AppHost.Start(e.Args);
        }
        catch (Exception ex)
        {
            try { Log.Error("startup", ex); } catch { /* 日志还没准备好 */ }
            MessageBox.Show(ex.Message, Brand.Name);
            Shutdown(1);
        }
    }

    static void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("dispatcher", e.Exception);
        try { AppHost.State?.Flush(); } catch { /* 已经在处理异常 */ }
        e.Handled = true;
        if (AppHost.IsSmoke)
        {
            Current.Shutdown(1);
            return;
        }
        MessageBox.Show("出了点问题，布局已经尽量保存。", Brand.Name);
    }
}
