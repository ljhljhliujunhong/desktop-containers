using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopContainers;

public sealed class ContainerWindow : Window
{
    readonly bool _placeOnCursor;
    readonly int _stagger;
    readonly Grid _layout = new();
    readonly WrapPanel _iconPanel = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };
    readonly ScrollViewer _scroller;
    readonly TextBlock _title = new();
    readonly Border _titleChip = new() { HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBox _editor = new();
    readonly TextBlock _empty;
    readonly Border _card;
    readonly GlassSurface _glass = new();
    readonly Border _shadow1;
    readonly Border _shadow2;
    readonly CornerGrip _grip = new();
    bool _resizing;
    bool _fitting;
    double _cellW = 84;
    double _cellH = 84;
    readonly Button _more;
    readonly TextBlock _lockMark;
    readonly StackPanel _chrome;
    readonly ContextMenu _menu = new();
    readonly List<AppIconView> _icons = new();
    bool _hover;
    bool _dragOver;
    bool _renaming;
    bool _armDrag;
    bool _dragging;
    bool _rebuildQueued;
    NativeMethods.POINT _armPoint;
    NativeMethods.POINT _resizeStart;
    double _armLeft;
    double _armTop;
    double _originWidth;
    double _originHeight;

    public ContainerModel Model { get; }

    public ContainerWindow(ContainerModel model, bool placeOnCursor, int stagger)
    {
        Model = model;
        _placeOnCursor = placeOnCursor;
        _stagger = stagger;
        Title = model.Name;
        Width = model.Width;
        Height = model.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = false;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        FontFamily = UiKit.Font;
        if (!placeOnCursor && !model.HasPixelPosition)
        {
            Left = model.X;
            Top = model.Y;
        }

        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);

        _shadow1 = new Border { IsHitTestVisible = false, Margin = new Thickness(16, 20, 16, 8), Background = Paint.Brush(Color.FromArgb(0x18, 0, 0, 0)) };
        _shadow2 = new Border { IsHitTestVisible = false, Margin = new Thickness(9, 12, 9, 12), Background = Paint.Brush(Color.FromArgb(0x10, 0, 0, 0)) };
        _card = new Border
        {
            Margin = new Thickness(16),
            BorderThickness = new Thickness(1.5),
            BorderBrush = _glass.Rim,
            AllowDrop = true
        };
        _scroller = new ScrollViewer
        {
            Content = _iconPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            AllowDrop = true
        };
        _iconPanel.Background = Brushes.Transparent;
        _title.FontFamily = UiKit.Font;
        _title.FontWeight = FontWeights.SemiBold;
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _titleChip.Child = _title;
        _editor.FontFamily = UiKit.Font;
        _editor.FontSize = 15;
        _editor.Visibility = Visibility.Collapsed;
        _editor.MinWidth = 120;
        _editor.LostFocus += (_, _) => { if (_renaming) CommitRename(); };
        _editor.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { CommitRename(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CancelRename(); e.Handled = true; }
        };
        _empty = new TextBlock
        {
            Text = "拖到这里",
            FontFamily = UiKit.Font,
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Margin = new Thickness(0, 18, 0, 0)
        };
        _lockMark = new TextBlock
        {
            Text = "已锁",
            FontFamily = UiKit.Font,
            FontSize = 12,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        _more = new Button
        {
            Content = "···",
            Width = 32,
            Height = 28,
            FontSize = 16,
            Padding = new Thickness(0),
            Cursor = Cursors.Hand,
            FocusVisualStyle = null,
            Background = Paint.Brush(Color.FromArgb(0x66, 255, 255, 255)),
            BorderThickness = new Thickness(0),
            Template = UiKit.RoundTemplate()
        };
        _more.Click += (_, _) =>
        {
            FillMenu();
            _menu.PlacementTarget = _card;
            _menu.IsOpen = true;
        };
        _chrome = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8)
        };
        _chrome.Children.Add(_lockMark);
        _chrome.Children.Add(_more);
        _grip.MouseLeftButtonDown += OnGripDown;
        _grip.MouseMove += OnGripMove;
        _grip.MouseLeftButtonUp += OnGripUp;

        _layout.Children.Add(_titleChip);
        _layout.Children.Add(_editor);
        _layout.Children.Add(_scroller);
        var overlay = new Grid();
        overlay.Children.Add(_glass);
        overlay.Children.Add(_layout);
        overlay.Children.Add(_empty);
        overlay.Children.Add(_chrome);
        overlay.Children.Add(_grip);
        _card.Child = overlay;
        _card.ContextMenu = _menu;
        _card.ContextMenuOpening += (_, _) => FillMenu();
        _card.MouseEnter += (_, _) => { _hover = true; ApplyChrome(); };
        _card.MouseLeave += (_, _) => { _hover = false; ApplyChrome(); };

        var root = new Grid();
        root.Children.Add(_shadow1);
        root.Children.Add(_shadow2);
        root.Children.Add(_card);
        Content = root;

        AllowDrop = true;
        DragOver += OnDragOver;
        DragLeave += OnDragLeave;
        Drop += OnDrop;
        SizeChanged += (_, _) => FitTitleWidth();

        model.PropertyChanged += OnModelChanged;
        model.Appearance.PropertyChanged += (_, args) =>
        {
            PaintCard();
            if (args.PropertyName is nameof(Appearance.TitleSize) or nameof(Appearance.TitleColor))
                ApplyTitle();
            if (args.PropertyName is nameof(Appearance.IconGap) or nameof(Appearance.TitleColor))
                ScheduleRebuild();
            if (args.PropertyName is nameof(Appearance.Padding) or nameof(Appearance.TitleSize))
                SnapToContents();
        };
        model.Apps.CollectionChanged += (_, _) => ScheduleRebuild();

        PaintCard();
        ApplyTitle();
        ApplyChrome();
        RebuildIcons();
        Loaded += OnLoaded;
    }

    public void ApplyChrome()
    {
        var edit = AppHost.State.Settings.EditMode && !Model.Locked;
        _grip.Visibility = edit ? Visibility.Visible : Visibility.Collapsed;
        _card.Cursor = Model.Locked ? Cursors.Arrow : Cursors.SizeAll;
        _more.Opacity = _hover || AppHost.State.Settings.EditMode ? 1 : 0;
        _lockMark.Visibility = Model.Locked && (_hover || AppHost.State.Settings.EditMode)
            ? Visibility.Visible
            : Visibility.Collapsed;
        _lockMark.Foreground = Paint.Brush(Paint.Hex(Model.Appearance.TitleColor));
        _card.BorderThickness = new Thickness(edit || _dragOver ? 2 : 1.5);
        _glass.Sync(Model.Appearance, _hover || _dragOver, AppHost.State.Settings.AnimationsEnabled);
    }

    public void ShowGlass(bool hot)
    {
        _hover = hot;
        _glass.Sync(Model.Appearance, hot, false);
        UpdateLayout();
    }

    public void Nudge()
    {
        var scale = _card.RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1);
        _card.RenderTransform = scale;
        _card.RenderTransformOrigin = new Point(0.5, 0.5);
        scale.ScaleX = 0.98;
        scale.ScaleY = 0.98;
        Motion.Scale(scale, 1, AppHost.State.Settings.AnimationsEnabled);
    }

    public void ClampAndSave()
    {
        MonitorGuard.ClampIfOffscreen(this);
        if (!AppHost.SuppressPersist)
            PersistBounds();
    }

    public static bool CursorInsideAny()
    {
        if (!NativeMethods.GetCursorPos(out var point)) return false;
        foreach (var window in AppHost.Windows)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var rect)) continue;
            if (point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom)
                return true;
        }
        return false;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        style |= NativeMethods.WS_EX_TOOLWINDOW;
        style &= ~NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style));
        NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER |
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);

        if (_placeOnCursor)
            MonitorGuard.CenterOnCursor(this, _stagger);
        else if (Model.HasPixelPosition)
        {
            NativeMethods.SetWindowPos(
                hwnd,
                IntPtr.Zero,
                Model.PixelX,
                Model.PixelY,
                0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }

        if (!Model.HasPixelPosition)
            MonitorGuard.NudgeApart(this);
        MonitorGuard.ClampIfOffscreen(this);
        DesktopPlacement.Register(hwnd);
        PersistBounds();
    }

    protected override void OnClosed(EventArgs e)
    {
        _glass.Stop();
        DesktopPlacement.Unregister(new WindowInteropHelper(this).Handle);
        if (!AppHost.SuppressPersist)
            PersistBounds();
        base.OnClosed(e);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        base.OnStateChanged(e);
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if (_renaming) return;
        var source = e.OriginalSource as DependencyObject;
        if (IsUnder(typeof(AppIconView), source) || IsUnder(typeof(Button), source) || IsUnder(_grip, source))
            return;
        if (e.ClickCount >= 2 && (IsUnder(_title, source) || IsUnder(_titleChip, source)))
        {
            BeginRename();
            e.Handled = true;
            return;
        }
        if (Model.Locked) return;
        _armDrag = true;
        NativeMethods.GetCursorPos(out _armPoint);
        _armLeft = Left;
        _armTop = Top;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (!_armDrag || _dragging || e.LeftButton != MouseButtonState.Pressed) return;
        NativeMethods.GetCursorPos(out var now);
        if (Math.Abs(now.X - _armPoint.X) + Math.Abs(now.Y - _armPoint.Y) < 6) return;
        _dragging = true;
        _armDrag = false;
        DesktopPlacement.Suspend = true;
        CaptureMouse();
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        _armDrag = false;
        if (!_dragging) return;
        _dragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        DesktopPlacement.Suspend = false;
        DesktopPlacement.PinAll();
        PersistBounds();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        NativeMethods.GetCursorPos(out var now);
        Left = _armLeft + (now.X - _armPoint.X) * DipScaleX();
        Top = _armTop + (now.Y - _armPoint.Y) * DipScaleY();
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        SnapToContents();
        if (!AppHost.State.Settings.AnimationsEnabled) return;
        var scale = new ScaleTransform(0.97, 0.97);
        _card.RenderTransform = scale;
        _card.RenderTransformOrigin = new Point(0.5, 0.5);
        _card.Opacity = 0;
        Motion.Fade(_card, 1, true);
        Motion.Scale(scale, 1, true);
    }

    void PaintCard()
    {
        var appearance = Model.Appearance;
        _card.Background = AppearancePainter.Background(appearance);
        _card.BorderBrush = _glass.Rim;
        _card.CornerRadius = new CornerRadius(appearance.CornerRadius);
        _card.Padding = new Thickness(0);
        _layout.Margin = new Thickness(appearance.Padding);
        _glass.Sync(appearance, _hover || _dragOver, false);
        _shadow1.CornerRadius = new CornerRadius(appearance.CornerRadius + 8);
        _shadow2.CornerRadius = new CornerRadius(appearance.CornerRadius + 4);
        var shadow = appearance.Shadow ? Visibility.Visible : Visibility.Collapsed;
        _shadow1.Visibility = shadow;
        _shadow2.Visibility = shadow;
        _empty.Foreground = Paint.Brush(Paint.WithAlpha(Paint.Hex(appearance.TitleColor), 0.55));
        ApplyChrome();
    }

    void ApplyTitle()
    {
        _title.Text = Model.Name;
        Title = Model.Name;
        _title.FontSize = Model.Appearance.TitleSize;
        var ink = Paint.Hex(Model.Appearance.TitleColor);
        _title.Foreground = Paint.Brush(ink);
        Paint.ApplyTitlePlate(_titleChip, ink);
        _editor.Foreground = _title.Foreground;
        var show = Model.ShowTitle && Model.TitlePlacement != TitlePlacement.Hidden;
        var titleVisible = show && !_renaming ? Visibility.Visible : Visibility.Collapsed;
        _title.Visibility = titleVisible;
        _titleChip.Visibility = titleVisible;
        _layout.RowDefinitions.Clear();

        if (!show)
        {
            _layout.RowDefinitions.Add(Star());
            Grid.SetRow(_scroller, 0);
            Grid.SetRow(_titleChip, 0);
            Grid.SetRow(_editor, 0);
            return;
        }

        switch (Model.TitlePlacement)
        {
            case TitlePlacement.Bottom:
                PlaceTitle(HorizontalAlignment.Center, new Thickness(4, 10, 4, 0), bottom: true);
                break;
            case TitlePlacement.TopLeft:
                PlaceTitle(HorizontalAlignment.Left, new Thickness(4, 0, 4, 8), bottom: false);
                break;
            case TitlePlacement.Center:
                _titleChip.HorizontalAlignment = HorizontalAlignment.Center;
                _titleChip.Margin = new Thickness(4, 0, 4, 8);
                _editor.HorizontalAlignment = HorizontalAlignment.Center;
                _editor.Margin = _titleChip.Margin;
                _layout.RowDefinitions.Add(Star());
                _layout.RowDefinitions.Add(Auto());
                _layout.RowDefinitions.Add(Auto());
                _layout.RowDefinitions.Add(Star());
                Grid.SetRow(_titleChip, 1);
                Grid.SetRow(_editor, 1);
                Grid.SetRow(_scroller, 2);
                break;
            default:
                PlaceTitle(HorizontalAlignment.Center, new Thickness(4, 0, 4, 8), bottom: false);
                break;
        }
    }

    void PlaceTitle(HorizontalAlignment alignment, Thickness margin, bool bottom)
    {
        _titleChip.HorizontalAlignment = alignment;
        _titleChip.Margin = margin;
        _editor.HorizontalAlignment = alignment;
        _editor.Margin = margin;
        _layout.RowDefinitions.Add(bottom ? Star() : Auto());
        _layout.RowDefinitions.Add(bottom ? Auto() : Star());
        Grid.SetRow(_scroller, bottom ? 0 : 1);
        Grid.SetRow(_titleChip, bottom ? 1 : 0);
        Grid.SetRow(_editor, bottom ? 1 : 0);
    }

    void RebuildIcons()
    {
        foreach (var icon in _icons) icon.Detach();
        _icons.Clear();
        _iconPanel.Children.Clear();
        foreach (var app in Model.Apps)
        {
            var view = new AppIconView(Model, app);
            _icons.Add(view);
            _iconPanel.Children.Add(view);
        }
        _empty.Visibility = Model.Apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SnapToContents();
    }

    void ScheduleRebuild()
    {
        if (_rebuildQueued) return;
        _rebuildQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _rebuildQueued = false;
            RebuildIcons();
        }, DispatcherPriority.Background);
    }

    void BeginRename()
    {
        _renaming = true;
        _editor.Text = Model.Name;
        _editor.Visibility = Visibility.Visible;
        _title.Visibility = Visibility.Collapsed;
        _titleChip.Visibility = Visibility.Collapsed;
        _editor.Focus();
        _editor.SelectAll();
    }

    void CommitRename()
    {
        if (!_renaming) return;
        _renaming = false;
        _editor.Visibility = Visibility.Collapsed;
        AppHost.State.Rename(Model, _editor.Text);
        ApplyTitle();
    }

    void CancelRename()
    {
        _renaming = false;
        _editor.Visibility = Visibility.Collapsed;
        ApplyTitle();
    }

    void FillMenu()
    {
        _menu.Items.Clear();
        _menu.Items.Add(Item("重命名", BeginRename));
        _menu.Items.Add(Item("外观", () => AppHost.ShowManager("containers", Model.Id)));
        var sizes = new MenuItem { Header = "图标大小" };
        sizes.Items.Add(Item("小", () => Model.IconSize = 40));
        sizes.Items.Add(Item("中", () => Model.IconSize = 56));
        sizes.Items.Add(Item("大", () => Model.IconSize = 80));
        _menu.Items.Add(sizes);
        _menu.Items.Add(Item(Model.Locked ? "解锁" : "锁定", () =>
        {
            Model.Locked = !Model.Locked;
            AppHost.State.Flush();
        }));
        _menu.Items.Add(Item(Model.ShowTitle ? "隐藏标题" : "显示标题", () =>
        {
            Model.ShowTitle = !Model.ShowTitle;
            if (Model.ShowTitle && Model.TitlePlacement == TitlePlacement.Hidden)
                Model.TitlePlacement = TitlePlacement.Top;
            AppHost.State.Flush();
        }));
        _menu.Items.Add(Item(Model.ShowNames ? "隐藏名称" : "显示名称", () =>
        {
            Model.ShowNames = !Model.ShowNames;
            AppHost.State.Flush();
        }));
        _menu.Items.Add(Item("整理图标", () => AppHost.State.SortApps(Model)));
        _menu.Items.Add(new Separator());
        var editing = AppHost.State.Settings.EditMode;
        _menu.Items.Add(Item(editing ? "结束编辑" : "编辑桌面", () => AppHost.SetEditMode(!editing)));
        _menu.Items.Add(Item("删除容器", ConfirmDelete));
    }

    static MenuItem Item(string text, Action action)
    {
        var item = new MenuItem { Header = text };
        item.Click += (_, _) => action();
        return item;
    }

    void ConfirmDelete()
    {
        var message = Model.Apps.Count == 0
            ? "删除这个容器？"
            : "能用的快捷方式会回到桌面，程序不会被卸掉。";
        if (!MiniDialog.Confirm(null, "删除「" + Model.Name + "」", message, "删除"))
            return;
        var error = AppHost.State.DeleteContainer(Model);
        if (error != null)
            MiniDialog.Alert(null, "没删掉", error);
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        if (Model.Locked || !CanDrop(e.Data))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            SetDragOver(false);
            return;
        }
        e.Effects = e.Data.GetDataPresent(DragSession.Format) ? DragDropEffects.Move : DragDropEffects.Copy;
        e.Handled = true;
        SetDragOver(true);
    }

    void OnDragLeave(object sender, DragEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (pos.X < 0 || pos.Y < 0 || pos.X > ActualWidth || pos.Y > ActualHeight)
            SetDragOver(false);
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        SetDragOver(false);
        if (Model.Locked) return;
        if (e.Data.GetDataPresent(DragSession.Format) && e.Data.GetData(DragSession.Format) is string raw)
        {
            var parts = raw.Split('|');
            if (parts.Length == 2)
                DragSession.Current?.Consume(parts[0], parts[1], Model.Id, IndexAt(e));
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = e.Data.GetData(DataFormats.FileDrop) as string[] ?? [];
        var before = Model.Apps.Count;
        var error = AppHost.State.ImportFiles(Model, files);
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        if (error != null) MiniDialog.Alert(null, "没放进去", error);
        else if (Model.Apps.Count != before) Nudge();
    }

    bool CanDrop(IDataObject data) =>
        data.GetDataPresent(DragSession.Format) || data.GetDataPresent(DataFormats.FileDrop);

    int IndexAt(DragEventArgs e)
    {
        var point = e.GetPosition(_iconPanel);
        for (var i = 0; i < _icons.Count; i++)
        {
            var top = _icons[i].TranslatePoint(new Point(0, 0), _iconPanel);
            if (new Rect(top, _icons[i].RenderSize).Contains(point))
                return i;
        }
        return Model.Apps.Count;
    }

    void SetDragOver(bool value)
    {
        if (_dragOver == value) return;
        _dragOver = value;
        PaintCard();
    }

    void OnGripDown(object sender, MouseButtonEventArgs e)
    {
        if (!AppHost.State.Settings.EditMode || Model.Locked) return;
        _resizing = true;
        MeasureCells();
        NativeMethods.GetCursorPos(out _resizeStart);
        _originWidth = Width;
        _originHeight = Height;
        _grip.CaptureMouse();
        DesktopPlacement.Suspend = true;
        e.Handled = true;
    }

    void OnGripMove(object sender, MouseEventArgs e)
    {
        if (!_resizing) return;
        NativeMethods.GetCursorPos(out var now);
        var wantW = _originWidth + (now.X - _resizeStart.X) * DipScaleX();
        var wantH = _originHeight + (now.Y - _resizeStart.Y) * DipScaleY();
        ApplyArrangement(BestColumns(wantW, wantH));
    }

    void OnGripUp(object sender, MouseButtonEventArgs e)
    {
        if (!_resizing) return;
        _resizing = false;
        _grip.ReleaseMouseCapture();
        DesktopPlacement.Suspend = false;
        DesktopPlacement.PinAll();
        PersistBounds();
        e.Handled = true;
    }

    void SnapToContents()
    {
        if (_fitting || _resizing) return;
        _fitting = true;
        try
        {
            MeasureCells();
            var beforeW = Model.Width;
            var beforeH = Model.Height;
            ApplyArrangement(BestColumns(Width, Height));
            if (!IsLoaded || double.IsNaN(Left) || double.IsNaN(Top) || Left < -10000 || Top < -10000) return;
            if (Math.Abs(beforeW - Width) < 0.5 && Math.Abs(beforeH - Height) < 0.5) return;
            PersistBounds();
            AppHost.State.Flush();
        }
        finally
        {
            _fitting = false;
        }
    }

    void MeasureCells()
    {
        var gap = Model.Appearance.IconGap;
        _cellW = Model.IconSize + 28 + gap;
        _cellH = Model.IconSize + gap;
        if (Model.ShowNames) _cellH += 22;
        if (Model.Apps.Any(app => app.Missing)) _cellH += 17;
        if (_icons.Count == 0) return;
        var width = 0d;
        var height = 0d;
        foreach (var icon in _icons)
        {
            icon.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            width = Math.Max(width, icon.DesiredSize.Width);
            height = Math.Max(height, icon.DesiredSize.Height);
        }
        if (width >= 8) _cellW = width;
        if (height >= 8) _cellH = height;
    }

    int BestColumns(double wantW, double wantH)
    {
        var count = Model.Apps.Count;
        if (count <= 1) return Math.Max(count, 0);
        var best = 1;
        var bestDist = double.MaxValue;
        for (var columns = 1; columns <= count; columns++)
        {
            var rows = (count + columns - 1) / columns;
            var dw = WindowWidth(columns) - wantW;
            var dh = WindowHeight(columns, rows) - wantH;
            var dist = dw * dw + dh * dh;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = columns;
            }
        }
        return best;
    }

    void ApplyArrangement(int columns)
    {
        if (Model.Apps.Count == 0 || columns < 1 || _cellW < 1 || _cellH < 1)
        {
            Width = 440;
            Height = 320;
            _iconPanel.Width = double.NaN;
            return;
        }

        var rows = (Model.Apps.Count + columns - 1) / columns;
        _iconPanel.Width = columns * _cellW + ContentSlack;
        Width = WindowWidth(columns);
        Height = WindowHeight(columns, rows);
    }

    const double ContentSlack = 2;

    double WindowWidth(int columns) =>
        Math.Max(FrameWidth() + columns * _cellW + ContentSlack, FrameWidth() + NaturalTitleWidth());

    double NaturalTitleWidth()
    {
        if (!Model.ShowTitle || Model.TitlePlacement == TitlePlacement.Hidden) return 0;
        var cap = _title.MaxWidth;
        _title.MaxWidth = 240;
        _titleChip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _title.MaxWidth = cap;
        return _titleChip.DesiredSize.Width + _titleChip.Margin.Left + _titleChip.Margin.Right;
    }

    void FitTitleWidth()
    {
        var inner = _card.ActualWidth
            - _card.BorderThickness.Left - _card.BorderThickness.Right
            - Model.Appearance.Padding * 2
            - _titleChip.Margin.Left - _titleChip.Margin.Right
            - _titleChip.Padding.Left - _titleChip.Padding.Right
            - _titleChip.BorderThickness.Left - _titleChip.BorderThickness.Right;
        _title.MaxWidth = Math.Max(48, inner);
    }

    double WindowHeight(int columns, int rows) => FrameHeight(columns * _cellW) + rows * _cellH + ContentSlack;

    double FrameWidth() =>
        _card.Margin.Left + _card.Margin.Right
        + _card.BorderThickness.Left + _card.BorderThickness.Right
        + Model.Appearance.Padding * 2;

    double FrameHeight(double contentWidth)
    {
        var frame = _card.Margin.Top + _card.Margin.Bottom
            + _card.BorderThickness.Top + _card.BorderThickness.Bottom
            + Model.Appearance.Padding * 2;
        if (Model.ShowTitle && Model.TitlePlacement != TitlePlacement.Hidden)
        {
            var textWidth = Math.Max(40, contentWidth - _titleChip.Margin.Left - _titleChip.Margin.Right);
            _titleChip.Measure(new Size(textWidth, double.PositiveInfinity));
            frame += _titleChip.DesiredSize.Height + _titleChip.Margin.Top + _titleChip.Margin.Bottom;
        }
        return frame;
    }

    void PersistBounds()
    {
        if (AppHost.SuppressPersist || Width < 10 || Height < 10) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero && NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            Model.PixelX = rect.Left;
            Model.PixelY = rect.Top;
            Model.HasPixelPosition = true;
            Model.MonitorDevice = MonitorGuard.DeviceName(this);
        }
        Model.X = Left;
        Model.Y = Top;
        Model.Width = Width;
        Model.Height = Height;
        Model.UpdatedUtc = DateTime.UtcNow;
        AppHost.State.RequestSave();
    }

    void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ContainerModel.Name):
                ApplyTitle();
                break;
            case nameof(ContainerModel.ShowTitle):
            case nameof(ContainerModel.TitlePlacement):
                ApplyTitle();
                SnapToContents();
                break;
            case nameof(ContainerModel.IconSize):
            case nameof(ContainerModel.ShowNames):
                ScheduleRebuild();
                AppHost.State.RequestSave();
                break;
            case nameof(ContainerModel.Locked):
                ApplyChrome();
                break;
        }
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_MOUSEACTIVATE = 0x0021;
        const int MA_NOACTIVATE = 3;
        const int WM_SYSCOMMAND = 0x0112;
        const int SC_MINIMIZE = 0xF020;
        const int WM_WINDOWPOSCHANGING = 0x0046;
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        }
        if (msg == WM_SYSCOMMAND && (wParam.ToInt32() & 0xFFF0) == SC_MINIMIZE)
        {
            handled = true;
            return IntPtr.Zero;
        }
        if (msg == WM_WINDOWPOSCHANGING)
        {
            var pos = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);
            if (pos.x == -32000 || pos.y == -32000)
            {
                pos.flags |= NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE;
                Marshal.StructureToPtr(pos, lParam, false);
            }
        }
        return IntPtr.Zero;
    }

    double DipScaleX() => Scale(true);
    double DipScaleY() => Scale(false);

    double Scale(bool horizontal)
    {
        if (PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
        {
            var matrix = target.TransformFromDevice;
            var value = horizontal ? matrix.M11 : matrix.M22;
            if (value > 0) return value;
        }
        return 1;
    }

    static bool IsUnder(Type type, DependencyObject? origin)
    {
        while (origin != null)
        {
            if (type.IsInstanceOfType(origin)) return true;
            origin = ParentOf(origin);
        }
        return false;
    }

    static bool IsUnder(DependencyObject target, DependencyObject? origin)
    {
        while (origin != null)
        {
            if (ReferenceEquals(origin, target)) return true;
            origin = ParentOf(origin);
        }
        return false;
    }

    static DependencyObject? ParentOf(DependencyObject node)
    {
        if (node is Visual || node is System.Windows.Media.Media3D.Visual3D)
            return VisualTreeHelper.GetParent(node);
        if (node is FrameworkContentElement content)
            return content.Parent;
        return null;
    }

    static RowDefinition Star() => new() { Height = new GridLength(1, GridUnitType.Star) };
    static RowDefinition Auto() => new() { Height = GridLength.Auto };
}
