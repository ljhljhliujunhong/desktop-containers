using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DesktopContainers;

public static class MiniDialog
{
    public static bool Confirm(Window? owner, string title, string message, string ok = "确定") =>
        Show(owner, title, message, ok, "取消", false, "", out _);

    public static void Alert(Window? owner, string title, string message) =>
        Show(owner, title, message, "好", null, false, "", out _);

    public static string? Prompt(Window? owner, string title, string initial, string ok = "保存") =>
        Show(owner, title, null, ok, "取消", true, initial, out var text) ? text : null;

    static bool Show(Window? owner, string title, string? message, string ok, string? cancel, bool prompt, string initial, out string text)
    {
        text = initial;
        var window = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = owner == null,
            FontFamily = UiKit.Font,
            WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Topmost = owner == null
        };
        if (owner != null) window.Owner = owner;

        var (field, input) = UiKit.Field(initial);
        field.Margin = new Thickness(0, message == null ? 16 : 8, 0, 0);
        field.Visibility = prompt ? Visibility.Visible : Visibility.Collapsed;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        if (cancel != null)
        {
            var cancelButton = UiKit.Button(cancel, () => window.DialogResult = false, ButtonKind.Ghost);
            cancelButton.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(cancelButton);
        }
        buttons.Children.Add(UiKit.Button(ok, () => window.DialogResult = true, ButtonKind.Primary));

        var body = new StackPanel();
        body.Children.Add(UiKit.Text(title, 18, FontWeights.SemiBold, Paint.Ink));
        if (!string.IsNullOrWhiteSpace(message))
        {
            var block = UiKit.Text(message, 14, FontWeights.Normal, Paint.Muted);
            block.TextWrapping = TextWrapping.Wrap;
            block.Margin = new Thickness(0, 10, 0, 0);
            block.TextTrimming = TextTrimming.None;
            body.Children.Add(block);
        }
        if (prompt) body.Children.Add(field);
        body.Children.Add(buttons);

        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = Paint.Brush(Color.FromRgb(0xE8, 0x5A, 0x8C)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(22),
            Margin = new Thickness(14),
            Child = body
        };
        var root = new Grid();
        root.Children.Add(new Border
        {
            Background = Paint.Brush(Color.FromArgb(0x55, 0x5A, 0x30, 0x40)),
            CornerRadius = new CornerRadius(26),
            Margin = new Thickness(16, 20, 16, 12),
            IsHitTestVisible = false
        });
        root.Children.Add(card);
        window.Content = root;
        if (prompt)
        {
            window.Loaded += (_, _) =>
            {
                input.Focus();
                input.SelectAll();
            };
            input.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                window.DialogResult = true;
            };
        }

        using var dim = ModalDim.Cover(owner);
        var accepted = window.ShowDialog() == true;
        text = input.Text;
        return accepted;
    }
}
