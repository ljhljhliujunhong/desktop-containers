using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace DesktopContainers;

public partial class ManagerWindow : Window
{
    readonly Dictionary<string, Button> _nav = new();
    readonly Dictionary<string, Border> _themeFrames = new();
    string _page = "home";
    string? _focusContainerId;
    string? _selectedThemeId;
    Appearance _draft;
    bool _loadingDraft;
    Grid _content = null!;
    ToggleSwitch? _editSwitch;
    Border? _previewHost;
    TextBox? _color1;
    TextBox? _color2;
    TextBox? _color3;
    TextBox? _borderColor;
    TextBox? _titleColor;
    Slider? _opacitySlider;
    Slider? _borderOpacitySlider;
    Slider? _radiusSlider;
    Slider? _titleSizeSlider;
    Slider? _paddingSlider;
    Slider? _gapSlider;
    ToggleSwitch? _shadowSwitch;
    Button? _updateThemeButton;

    public ManagerWindow()
    {
        var themes = AppHost.State.Document.CustomThemes;
        _selectedThemeId = AppHost.State.Settings.DefaultThemeId;
        _draft = ThemeCatalog.CreateAppearance(_selectedThemeId, themes);
        Title = Brand.Name;
        Width = 1080;
        Height = 720;
        MinWidth = 960;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Paint.Brush(Paint.Paper);
        FontFamily = UiKit.Font;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Icon = BrandIcon.CreateBitmap(64);
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false,
            CornerRadius = new CornerRadius(0)
        });
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Content = BuildShell();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var preference = 2;
            _ = NativeMethods.DwmSetWindowAttribute(hwnd, 33, ref preference, 4);
            HwndSource.FromHwnd(hwnd)?.AddHook(KeepCaptionLight);
        };
        Navigate("home");
    }

    public void Open(string page, string? containerId)
    {
        if (!string.IsNullOrWhiteSpace(containerId))
        {
            _focusContainerId = containerId;
            var container = AppHost.State.Find(containerId);
            if (container != null && page == "themes")
            {
                _draft = container.Appearance.Clone();
                _selectedThemeId = container.ThemeId;
            }
        }
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        Navigate(page);
    }

    public void SyncEditSwitch() => _editSwitch?.SetSilent(AppHost.State.Settings.EditMode);

    public void CreateContainer()
    {
        using (ModalDim.Cover(this))
        {
            var dialog = new NewContainerWindow(false, this);
            if (dialog.ShowDialog() == true)
                Navigate(_page);
        }
    }

    public void Navigate(string page)
    {
        _page = page;
        MarkNav();
        _content.Children.Clear();
        UIElement body = page switch
        {
            "containers" => BuildContainers(),
            "themes" => BuildThemes(),
            "settings" => BuildSettings(),
            _ => BuildHome()
        };
        _content.Children.Add(page is "themes" or "containers" ? body : UiKit.Scroll(body));
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AppHost.IsExiting)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    UIElement BuildShell()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var caption = new Grid { Background = Brushes.White };
        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(18, 0, 0, 0)
        };
        brand.Children.Add(new Image
        {
            Source = BrandIcon.CreateBitmap(128),
            Width = 28,
            Height = 28,
            Margin = new Thickness(0, 0, 10, 0)
        });
        var brandText = UiKit.Text(Brand.Name, 15, FontWeights.SemiBold, Paint.Ink);
        brandText.VerticalAlignment = VerticalAlignment.Center;
        brand.Children.Add(brandText);
        caption.Children.Add(brand);

        var windowButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        windowButtons.Children.Add(CaptionButton("—", () => WindowState = WindowState.Minimized, false));
        windowButtons.Children.Add(CaptionButton("×", Close, true));
        caption.Children.Add(windowButtons);
        caption.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && FindsButton(source)) return;
            try { DragMove(); } catch (InvalidOperationException) { /* 鼠标已经松开 */ }
        };

        var body = new Grid();
        Grid.SetRow(body, 1);
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(216) });
        body.ColumnDefinitions.Add(new ColumnDefinition());

        var sidebar = new DockPanel { Background = Brushes.White };
        var nav = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        nav.Children.Add(Nav("home", "首页"));
        nav.Children.Add(Nav("containers", "容器"));
        nav.Children.Add(Nav("themes", "主题"));
        nav.Children.Add(Nav("settings", "设置"));

        var footer = new StackPanel { Margin = new Thickness(18, 12, 18, 18) };
        DockPanel.SetDock(footer, Dock.Bottom);
        sidebar.Children.Add(footer);
        sidebar.Children.Add(nav);
        var editRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        editRow.ColumnDefinitions.Add(new ColumnDefinition());
        editRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var editLabel = UiKit.Text("编辑桌面", 13, FontWeights.Normal, Paint.Ink);
        editLabel.VerticalAlignment = VerticalAlignment.Center;
        _editSwitch = new ToggleSwitch(AppHost.State.Settings.EditMode, on => AppHost.SetEditMode(on));
        Grid.SetColumn(_editSwitch, 1);
        editRow.Children.Add(editLabel);
        editRow.Children.Add(_editSwitch);
        footer.Children.Add(editRow);
        var exit = UiKit.Button("退出", AppHost.Exit, ButtonKind.Ghost, hover: true);
        exit.HorizontalAlignment = HorizontalAlignment.Stretch;
        footer.Children.Add(exit);

        _content = new Grid();
        Grid.SetColumn(_content, 1);
        body.Children.Add(sidebar);
        body.Children.Add(_content);
        root.Children.Add(caption);
        root.Children.Add(body);
        return root;
    }

    Button Nav(string page, string label)
    {
        var button = UiKit.Button(label, () => Navigate(page), ButtonKind.Ghost, hover: false);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.Margin = new Thickness(12, 2, 12, 2);
        button.Padding = new Thickness(14, 10, 14, 10);
        button.MouseEnter += (_, _) =>
        {
            if ((string?)button.Tag != "on")
                button.Background = Paint.Brush(Color.FromRgb(0xFF, 0xF0, 0xF6));
        };
        button.MouseLeave += (_, _) => PaintNav(button, (string?)button.Tag == "on");
        _nav[page] = button;
        return button;
    }

    void MarkNav()
    {
        foreach (var (page, button) in _nav)
        {
            var on = page == _page;
            button.Tag = on ? "on" : "off";
            PaintNav(button, on);
        }
    }

    static void PaintNav(Button button, bool on)
    {
        button.Background = Paint.Brush(on ? Color.FromRgb(0xFF, 0xE4, 0xF1) : Colors.Transparent);
        button.Foreground = Paint.Brush(on ? Color.FromRgb(0xC7, 0x3B, 0x6F) : Paint.Ink);
        button.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
    }

    static bool FindsButton(DependencyObject source)
    {
        while (source != null)
        {
            if (source is Button) return true;
            source = VisualTreeHelper.GetParent(source) ?? LogicalTreeHelper.GetParent(source);
        }
        return false;
    }

    static IntPtr KeepCaptionLight(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_NCACTIVATE = 0x0086;
        if (msg != WM_NCACTIVATE) return IntPtr.Zero;
        handled = true;
        return NativeMethods.DefWindowProc(hwnd, msg, new IntPtr(1), new IntPtr(-1));
    }

    static Button CaptionButton(string text, Action click, bool danger)
    {
        var button = new Button
        {
            Content = text,
            Width = 46,
            Height = 32,
            Margin = new Thickness(0, 0, 6, 0),
            FontSize = danger ? 16 : 14,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Paint.Brush(Paint.Ink),
            Cursor = System.Windows.Input.Cursors.Hand,
            FocusVisualStyle = null,
            Template = UiKit.RoundTemplate()
        };
        WindowChrome.SetIsHitTestVisibleInChrome(button, true);
        button.Click += (_, _) => click();
        button.MouseEnter += (_, _) => button.Background = Paint.Brush(danger ? Color.FromRgb(0xFF, 0xE4, 0xEA) : Color.FromRgb(0xF6, 0xEE, 0xF2));
        button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
        return button;
    }
}
