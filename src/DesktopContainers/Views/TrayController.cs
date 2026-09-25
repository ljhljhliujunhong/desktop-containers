namespace DesktopContainers;

public sealed class TrayController : IDisposable
{
    readonly System.Windows.Forms.NotifyIcon _icon;
    readonly System.Windows.Forms.ToolStripMenuItem _edit;

    public TrayController()
    {
        _edit = new System.Windows.Forms.ToolStripMenuItem("编辑桌面");
        _edit.Click += (_, _) => AppHost.SetEditMode(!AppHost.State.Settings.EditMode);

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("打开", null, (_, _) => AppHost.ShowManager());
        menu.Items.Add(_edit);
        menu.Items.Add("新建容器", null, (_, _) =>
        {
            AppHost.ShowManager();
            AppHost.Manager?.CreateContainer();
        });
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => AppHost.Exit());

        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = BrandIcon.CreateIcon(),
            Text = "桌面容器",
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => AppHost.ShowManager();
        Sync();
    }

    public void Sync() => _edit.Checked = AppHost.State.Settings.EditMode;

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
