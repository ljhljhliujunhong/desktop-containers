using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DesktopContainers;

public sealed class NewContainerWindow : Window
{
    readonly bool _onboarding;
    readonly TextBox _input;
    readonly TextBlock _hint;
    readonly Dictionary<string, Border> _frames = new();
    string _themeId;

    public NewContainerWindow(bool onboarding, Window? owner = null)
    {
        _onboarding = onboarding;
        _themeId = AppHost.State.Settings.DefaultThemeId;
        Title = onboarding ? "第一个容器" : "新建容器";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = UiKit.Font;
        ShowInTaskbar = false;
        if (owner != null)
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
        }

        var (field, input) = UiKit.Field("");
        _input = input;
        _hint = new TextBlock
        {
            Text = onboarding ? "AI 应用" : "新容器",
            FontFamily = UiKit.Font,
            FontSize = 15,
            Foreground = Paint.Brush(Paint.Muted),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
            IsHitTestVisible = false
        };
        var fieldHost = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        fieldHost.Children.Add(field);
        fieldHost.Children.Add(_hint);
        input.TextChanged += (_, _) => _hint.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        input.GotFocus += (_, _) => _hint.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Create();
        };

        var themes = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        foreach (var theme in ThemeCatalog.All(AppHost.State.Document.CustomThemes))
        {
            var frame = new Border
            {
                Padding = new Thickness(3),
                Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand,
                Child = PreviewCard.Create(theme.Appearance, theme.Name, "", null, 92, 64)
            };
            var id = theme.Id;
            frame.MouseLeftButtonUp += (_, _) => Select(id);
            _frames[id] = frame;
            themes.Children.Add(frame);
        }
        Select(_themeId);

        var themeScroll = new ScrollViewer
        {
            Content = themes,
            MaxHeight = 240,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var secondary = UiKit.Button(onboarding ? "以后再说" : "取消", () => DialogResult = false, ButtonKind.Ghost);
        secondary.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(secondary);
        buttons.Children.Add(UiKit.Button(onboarding ? "放上桌面" : "创建", Create, ButtonKind.Primary));

        var body = new StackPanel();
        body.Children.Add(UiKit.Text(Title, 22, FontWeights.SemiBold, Paint.Ink));
        body.Children.Add(fieldHost);
        body.Children.Add(themeScroll);
        body.Children.Add(buttons);

        var root = new Grid();
        root.Children.Add(new Border
        {
            Margin = new Thickness(16, 20, 16, 12),
            CornerRadius = new CornerRadius(28),
            Background = Paint.Brush(Color.FromArgb(0x18, 0, 0, 0)),
            IsHitTestVisible = false
        });
        root.Children.Add(new Border
        {
            Margin = new Thickness(16),
            Background = Brushes.White,
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(22),
            Child = body
        });
        Content = root;
        Loaded += (_, _) => input.Focus();
    }

    void Select(string id)
    {
        if (!_frames.ContainsKey(id))
            id = ThemeCatalog.DefaultId;
        _themeId = id;
        foreach (var (themeId, frame) in _frames)
            frame.BorderBrush = Paint.Brush(themeId == id ? Paint.Accent : Colors.Transparent);
    }

    void Create()
    {
        try
        {
            AppHost.State.CreateContainer(_input.Text, _themeId, true);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Log.Error("create", ex);
            MiniDialog.Alert(this, "没能创建", "再试一次。");
        }
    }
}
