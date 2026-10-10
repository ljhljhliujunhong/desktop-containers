using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DesktopContainers;

public partial class ManagerWindow
{
    UIElement BuildLayouts()
    {
        var document = AppHost.State.Document;
        var page = UiKit.Header("布局", UiKit.Button("记住当前位置", SaveNewLayout, ButtonKind.Primary));
        var startupRow = new StackPanel();
        startupRow.Children.Add(UiKit.Text("开机布局", 14, FontWeights.SemiBold, Paint.Ink));
        var startup = new ComboBox
        {
            Margin = new Thickness(0, 10, 0, 0), MinHeight = 34,
            FontFamily = UiKit.Font, FontSize = 14,
            DisplayMemberPath = nameof(LayoutChoice.Name), SelectedValuePath = nameof(LayoutChoice.Id)
        };
        var choices = new List<LayoutChoice> { new(null, "上次退出的位置") };
        choices.AddRange(document.LayoutProfiles.Select(profile => new LayoutChoice(profile.Id, profile.Name)));
        startup.ItemsSource = choices;
        startup.SelectedItem = choices.FirstOrDefault(choice => choice.Id == document.StartupLayoutId) ?? choices[0];
        startup.SelectionChanged += (_, _) => LayoutAction(() => LayoutProfiles.SetStartup((startup.SelectedItem as LayoutChoice)?.Id));
        startupRow.Children.Add(startup);
        page.Children.Add(UiKit.Card(startupRow, new Thickness(20)));
        page.Children.Add(Section("已记住的布局"));
        if (document.LayoutProfiles.Count == 0)
        {
            page.Children.Add(UiKit.Card(UiKit.Text("还没有保存的布局", 15, FontWeights.Normal, Paint.Muted), new Thickness(22)));
            return page;
        }
        foreach (var profile in document.LayoutProfiles.OrderByDescending(item => item.SavedUtc))
        {
            var remembered = DisplayMemory.Choose(profile.Screens, DisplayMemory.CurrentKey);
            var source = remembered == null ? null : DisplayMemory.PreviewLayout(remembered);
            var card = new Grid();
            card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            card.ColumnDefinitions.Add(new ColumnDefinition());
            card.Children.Add(LayoutPreview(source));
            var info = new StackPanel { Margin = new Thickness(20, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(info, 1);
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(UiKit.Text(profile.Name, 17, FontWeights.SemiBold, Paint.Ink));
            if (document.ActiveLayoutId == profile.Id)
            {
                var matches = source?.Spots.All(spot => AppHost.State.Find(spot.Id) is { } model
                    && Math.Abs(model.PixelX - spot.PixelX) <= 2 && Math.Abs(model.PixelY - spot.PixelY) <= 2
                    && Math.Abs(model.Width - spot.Width) <= 2 && Math.Abs(model.Height - spot.Height) <= 2) == true;
                var badge = UiKit.Text(matches ? "使用中" : "位置已调整", 12, FontWeights.Normal, Paint.Accent);
                badge.Margin = new Thickness(12, 3, 0, 0);
                title.Children.Add(badge);
            }
            info.Children.Add(title);
            var adaptation = remembered?.Key != DisplayMemory.CurrentKey ? " · 适配当前屏幕" : "";
            var detail = UiKit.Text($"{source?.Spots.Count ?? 0} 个容器 · {profile.Screens.Count} 种屏幕排列{adaptation} · {profile.SavedUtc.ToLocalTime():MM-dd HH:mm}",
                12, FontWeights.Normal, Paint.Muted);
            detail.Margin = new Thickness(0, 7, 0, 12);
            info.Children.Add(detail);
            var actions = new WrapPanel();
            actions.Children.Add(Small("使用", () => LayoutAction(() => DisplayMemory.ApplyProfile(profile)), true));
            actions.Children.Add(Small("更新当前位置", () =>
            {
                if (MiniDialog.Confirm(this, "更新布局", $"用当前位置替换「{profile.Name}」在当前屏幕下的排列？", "更新"))
                    LayoutAction(() => LayoutProfiles.Save(profile.Name, profile.Id));
            }, true));
            actions.Children.Add(Small("重命名", () =>
            {
                var name = MiniDialog.Prompt(this, "布局名称", profile.Name);
                if (name != null) LayoutAction(() => LayoutProfiles.Rename(profile, name));
            }, true));
            actions.Children.Add(Small("删除", () =>
            {
                if (MiniDialog.Confirm(this, "删除布局", $"删除「{profile.Name}」的布局记忆？桌面上的容器会保留。", "删除"))
                    LayoutAction(() => LayoutProfiles.Delete(profile));
            }, true));
            info.Children.Add(actions);
            card.Children.Add(info);
            var frame = UiKit.Card(card, new Thickness(20));
            frame.Margin = new Thickness(0, 0, 0, 14);
            page.Children.Add(frame);
        }
        return page;
    }

    void SaveNewLayout()
    {
        var profiles = AppHost.State.Document.LayoutProfiles;
        var index = profiles.Count + 1;
        while (profiles.Any(profile => profile.Name == "布局 " + index)) index++;
        var name = MiniDialog.Prompt(this, "布局名称", "布局 " + index);
        if (name != null) LayoutAction(() => LayoutProfiles.Save(name));
    }

    void LayoutAction(Action action)
    {
        try
        {
            action();
            Navigate("layouts");
        }
        catch (Exception ex)
        {
            Log.Error("layout action", ex);
            MiniDialog.Alert(this, "布局未完成", ex is InvalidOperationException ? ex.Message : "布局没能保存或恢复，请稍后再试。");
        }
    }

    static UIElement LayoutPreview(ScreenLayout? layout)
    {
        var canvas = new Canvas { Width = 160, Height = 96, ClipToBounds = true };
        if (layout is { Spots.Count: > 0 })
        {
            var left = Math.Min(0, layout.Spots.Min(spot => spot.PixelX));
            var top = Math.Min(0, layout.Spots.Min(spot => spot.PixelY));
            var right = Math.Max(left + 1, layout.Spots.Max(spot => spot.PixelX + Math.Max(1, spot.PixelWidth)));
            var bottom = Math.Max(top + 1, layout.Spots.Max(spot => spot.PixelY + Math.Max(1, spot.PixelHeight)));
            if (layout.Monitors.Count > 0)
            {
                left = Math.Min(left, layout.Monitors.Min(monitor => monitor.X));
                top = Math.Min(top, layout.Monitors.Min(monitor => monitor.Y));
                right = Math.Max(right, layout.Monitors.Max(monitor => monitor.X + monitor.Width));
                bottom = Math.Max(bottom, layout.Monitors.Max(monitor => monitor.Y + monitor.Height));
            }
            var scale = Math.Min(144.0 / (right - left), 80.0 / (bottom - top));
            foreach (var spot in layout.Spots)
            {
                var block = new Border
                {
                    Width = Math.Max(3, spot.PixelWidth * scale), Height = Math.Max(3, spot.PixelHeight * scale),
                    Background = Paint.Brush(Color.FromArgb(0x50, 0xE8, 0x5A, 0x8C)),
                    ToolTip = AppHost.State.Find(spot.Id)?.Name,
                    BorderBrush = Paint.Brush(Paint.Accent), BorderThickness = new Thickness(0.7), CornerRadius = new CornerRadius(3)
                };
                Canvas.SetLeft(block, 8 + (spot.PixelX - left) * scale);
                Canvas.SetTop(block, 8 + (spot.PixelY - top) * scale);
                canvas.Children.Add(block);
            }
        }
        return new Border
        {
            Child = canvas, Background = Paint.Brush(Paint.Paper), CornerRadius = new CornerRadius(12),
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    sealed record LayoutChoice(string? Id, string Name);
}
