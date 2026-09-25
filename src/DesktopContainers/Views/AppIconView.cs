using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DesktopContainers;

public sealed class AppIconView : StackPanel
{
    readonly ContainerModel _model;
    readonly AppEntry _entry;
    readonly Image _image;
    readonly TextBlock? _badge;
    bool _alive = true;
    Point? _down;

    public AppIconView(ContainerModel model, AppEntry entry)
    {
        _model = model;
        _entry = entry;
        Orientation = Orientation.Vertical;
        Cursor = Cursors.Hand;
        ToolTip = entry.DisplayName;
        Width = model.IconSize + 28;
        Margin = new Thickness(model.Appearance.IconGap / 2);

        var host = new Grid
        {
            Width = model.IconSize,
            Height = model.IconSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1)
        };
        host.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = Paint.Brush(Paint.WithAlpha(Paint.Hex(model.Appearance.TitleColor), 0.12)),
            Child = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(entry.DisplayName) ? "?" : entry.DisplayName.Trim()[..1],
                FontFamily = UiKit.Font,
                FontSize = Math.Max(12, model.IconSize * 0.28),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Paint.Brush(Paint.Hex(model.Appearance.TitleColor))
            }
        });
        _image = new Image { Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        host.Children.Add(_image);
        Children.Add(host);

        if (entry.Missing)
            _image.Opacity = 0.35;

        if (model.ShowNames)
        {
            Children.Add(new TextBlock
            {
                Text = entry.DisplayName,
                FontFamily = UiKit.Font,
                FontSize = 12,
                Foreground = Paint.Brush(Paint.Ink),
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 6, 0, 0)
            });
        }

        _badge = entry.Missing
            ? new TextBlock
            {
                Text = "找不到",
                FontFamily = UiKit.Font,
                FontSize = 11,
                Foreground = Paint.Brush(Paint.Muted),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            }
            : null;
        if (_badge != null) Children.Add(_badge);

        var scale = (ScaleTransform)host.RenderTransform;
        host.MouseEnter += (_, _) => Motion.Scale(scale, 1.06, AppHost.State.Settings.AnimationsEnabled);
        host.MouseLeave += (_, _) => Motion.Scale(scale, 1, AppHost.State.Settings.AnimationsEnabled);

        ContextMenuOpening += (_, _) => ContextMenu = BuildMenu();
        LoadIcon();
    }

    public void Detach() => _alive = false;

    void LoadIcon()
    {
        var path = AppHost.Shortcuts.PathOf(_entry);
        var cached = AppHost.Icons.GetCached(path, 256);
        if (cached != null)
        {
            _image.Source = cached;
            return;
        }
        AppHost.Icons.Load(path, 256, source =>
        {
            if (_alive && source != null)
                _image.Source = source;
        });
    }

    ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("打开", Launch));
        if (_entry.Missing)
            menu.Items.Add(Item("移除", () => AppHost.State.RemoveMissing(_model, _entry)));
        else if (!_model.Locked)
            menu.Items.Add(Item("移出到桌面", Restore));
        if (!string.IsNullOrWhiteSpace(_entry.TargetPath) &&
            (File.Exists(_entry.TargetPath) || Directory.Exists(_entry.TargetPath)))
            menu.Items.Add(Item("打开所在位置", () => AppHost.Shortcuts.Reveal(_entry)));
        return menu;
    }

    static MenuItem Item(string text, Action action)
    {
        var item = new MenuItem { Header = text };
        item.Click += (_, _) => action();
        return item;
    }

    void Launch()
    {
        if (AppHost.Shortcuts.TryLaunch(_entry)) return;
        _image.Opacity = 0.35;
        if (_badge == null)
        {
            Children.Add(new TextBlock
            {
                Text = "找不到",
                FontFamily = UiKit.Font,
                FontSize = 11,
                Foreground = Paint.Brush(Paint.Muted),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            });
        }
    }

    void Restore()
    {
        if (AppHost.State.RestoreApp(_model, _entry)) return;
        MiniDialog.Alert(null, "没能移出", "快捷方式还在容器里。");
    }

    void StartDrag()
    {
        var session = DragSession.Begin(_model.Id, _entry.Id);
        var data = new DataObject(DragSession.Format, _model.Id + "|" + _entry.Id);
        QueryContinueDragEventHandler query = (_, e) =>
        {
            if (e.EscapePressed || e.Action == DragAction.Cancel)
                session.Cancelled = true;
        };
        QueryContinueDrag += query;
        try
        {
            DragDrop.DoDragDrop(this, data, DragDropEffects.Move);
        }
        finally
        {
            QueryContinueDrag -= query;
            if (!session.Cancelled && !session.Consumed && !ContainerWindow.CursorInsideAny())
                AppHost.State.RestoreApp(_model, _entry);
            session.Complete();
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount >= 2)
        {
            _down = null;
            Launch();
            e.Handled = true;
            return;
        }
        _down = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_down == null || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(this) - _down.Value;
        if (Math.Abs(delta.X) + Math.Abs(delta.Y) < 8) return;
        _down = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (!AppHost.State.Settings.EditMode || _model.Locked) return;
        StartDrag();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _down = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }
}
