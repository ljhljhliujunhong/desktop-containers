using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DesktopContainers;

public static class PreviewCard
{
    public static Border Create(Appearance appearance, string title, string subtitle, IReadOnlyList<AppEntry>? apps, double width, double height)
    {
        var card = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(Math.Clamp(appearance.CornerRadius * 0.62, 12, 22)),
            Background = AppearancePainter.Background(appearance),
            BorderBrush = GlassSurface.StaticRim(appearance),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            ClipToBounds = true,
            SnapsToDevicePixels = true
        };

        var root = new DockPanel();
        var heading = new TextBlock
        {
            Text = title,
            FontFamily = UiKit.Font,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Paint.Brush(Paint.Hex(appearance.TitleColor)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var plate = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 6),
            Child = heading
        };
        Paint.ApplyTitlePlate(plate, Paint.Hex(appearance.TitleColor));
        DockPanel.SetDock(plate, Dock.Top);
        root.Children.Add(plate);

        var foot = new TextBlock
        {
            Text = subtitle,
            FontFamily = UiKit.Font,
            FontSize = 11,
            Foreground = Paint.Brush(Paint.WithAlpha(Paint.Hex(appearance.TitleColor), 0.72)),
            Margin = new Thickness(0, 6, 0, 0)
        };
        DockPanel.SetDock(foot, Dock.Bottom);
        root.Children.Add(foot);

        var wrap = new WrapPanel { VerticalAlignment = VerticalAlignment.Top };
        if (apps != null)
        {
            foreach (var app in apps.Take(8))
                wrap.Children.Add(MiniIcon(app, appearance));
        }
        root.Children.Add(wrap);
        card.Child = root;
        return card;
    }

    static FrameworkElement MiniIcon(AppEntry app, Appearance appearance)
    {
        var grid = new Grid { Width = 22, Height = 22, Margin = new Thickness(2) };
        var plate = new Border
        {
            Background = Paint.Brush(Paint.WithAlpha(Paint.Hex(appearance.TitleColor), 0.14)),
            CornerRadius = new CornerRadius(6),
            Child = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(app.DisplayName) ? "?" : app.DisplayName.Trim()[..1],
                FontSize = 11,
                FontFamily = UiKit.Font,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Paint.Brush(Paint.Hex(appearance.TitleColor))
            }
        };
        var image = new Image { Width = 22, Height = 22, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        grid.Children.Add(plate);
        grid.Children.Add(image);
        var path = AppHost.Shortcuts.PathOf(app);
        var cached = AppHost.Icons.GetCached(path, 48);
        if (cached != null) image.Source = cached;
        else AppHost.Icons.Load(path, 48, source => { if (source != null) image.Source = source; });
        if (app.Missing) image.Opacity = 0.4;
        return grid;
    }
}
