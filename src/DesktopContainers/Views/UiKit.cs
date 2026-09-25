using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace DesktopContainers;

public enum ButtonKind
{
    Primary,
    Soft,
    Ghost,
    Danger
}

public static class UiKit
{
    static ControlTemplate? _template;

    public static FontFamily Font { get; } = new("Segoe UI Variable Text, Microsoft YaHei UI, Segoe UI");

    public static TextBlock Text(string text, double size, FontWeight weight, Color color)
    {
        return new TextBlock
        {
            Text = text,
            FontFamily = Font,
            FontSize = size,
            FontWeight = weight,
            Foreground = Paint.Brush(color),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
    }

    public static Button Button(string text, Action click, ButtonKind kind = ButtonKind.Soft, bool hover = true)
    {
        var (background, foreground, border, hoverBg) = ColorsFor(kind);
        var button = new Button
        {
            Content = text,
            FontFamily = Font,
            FontSize = 13,
            Cursor = Cursors.Hand,
            FocusVisualStyle = null,
            Padding = new Thickness(14, 8, 14, 8),
            MinHeight = 36,
            Background = Paint.Brush(background),
            Foreground = Paint.Brush(foreground),
            BorderBrush = Paint.Brush(border),
            BorderThickness = new Thickness(kind == ButtonKind.Ghost ? 0 : 1),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Template = RoundTemplate()
        };
        if (hover)
        {
            button.MouseEnter += (_, _) => button.Background = Paint.Brush(hoverBg);
            button.MouseLeave += (_, _) => button.Background = Paint.Brush(background);
        }
        button.Click += (_, _) => click();
        return button;
    }

    public static ControlTemplate RoundTemplate()
    {
        if (_template != null) return _template;
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetBinding(FrameworkElement.HorizontalAlignmentProperty, new Binding("HorizontalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
        border.AppendChild(content);
        _template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        return _template;
    }

    public static (Border box, TextBox input) Field(string text)
    {
        var input = new TextBox
        {
            Text = text,
            FontSize = 15,
            FontFamily = Font,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var box = new Border
        {
            Background = Brushes.White,
            BorderBrush = Paint.Brush(Paint.Line),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 10, 12, 10),
            Child = input
        };
        input.GotFocus += (_, _) => box.BorderBrush = Paint.Brush(Paint.Accent);
        input.LostFocus += (_, _) => box.BorderBrush = Paint.Brush(Paint.Line);
        return (box, input);
    }

    public static (Grid row, Slider slider) SliderRow(string label, double min, double max, double value, bool fine = false)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fine ? 58 : 48) });
        var caption = Text(label, 13, FontWeights.Normal, Paint.Ink);
        caption.VerticalAlignment = VerticalAlignment.Center;
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
            Foreground = Paint.Brush(Paint.Accent),
            IsSnapToTickEnabled = false,
            IsMoveToPointEnabled = true,
            SmallChange = fine ? 0.001 : 1,
            LargeChange = fine ? 0.02 : Math.Max(1, (max - min) / 10)
        };
        var number = Text(FormatSlider(value, fine), 13, FontWeights.SemiBold, Paint.Muted);
        number.VerticalAlignment = VerticalAlignment.Center;
        number.HorizontalAlignment = HorizontalAlignment.Right;
        slider.ValueChanged += (_, _) => number.Text = FormatSlider(slider.Value, fine);
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(number, 2);
        grid.Children.Add(caption);
        grid.Children.Add(slider);
        grid.Children.Add(number);
        return (grid, slider);
    }

    public static Grid ToggleRow(string label, bool on, Action<bool> changed)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var caption = Text(label, 14, FontWeights.Normal, Paint.Ink);
        caption.VerticalAlignment = VerticalAlignment.Center;
        var toggle = new ToggleSwitch(on, changed) { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(caption);
        grid.Children.Add(toggle);
        return grid;
    }

    public static Border Card(UIElement child, Thickness? padding = null)
    {
        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Paint.Brush(Paint.Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Padding = padding ?? new Thickness(16),
            Child = child
        };
    }

    public static ScrollViewer Scroll(UIElement content)
    {
        return new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(28, 22, 28, 28)
        };
    }

    public static StackPanel Header(string title, UIElement? action = null)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = Text(title, 28, FontWeights.SemiBold, Paint.Ink);
        text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(text);
        if (action != null)
        {
            action.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            Grid.SetColumn(action, 1);
            row.Children.Add(action);
        }
        var panel = new StackPanel();
        panel.Children.Add(row);
        return panel;
    }

    static string FormatSlider(double value, bool fine) =>
        fine ? value.ToString("0.000") : value.ToString("0");

    static (Color bg, Color fg, Color border, Color hover) ColorsFor(ButtonKind kind) => kind switch
    {
        ButtonKind.Primary => (Paint.Accent, Colors.White, Paint.Accent, Color.FromRgb(0xD9, 0x48, 0x7C)),
        ButtonKind.Danger => (Colors.White, Color.FromRgb(0xC4, 0x47, 0x67), Color.FromRgb(0xF3, 0xD0, 0xDA), Color.FromRgb(0xFF, 0xF1, 0xF4)),
        ButtonKind.Ghost => (Colors.Transparent, Paint.Ink, Colors.Transparent, Color.FromRgb(0xFF, 0xF0, 0xF6)),
        _ => (Colors.White, Paint.Ink, Paint.Line, Color.FromRgb(0xFF, 0xF0, 0xF6))
    };
}

public sealed class ToggleSwitch : Border
{
    readonly Border _knob;
    readonly Action<bool>? _changed;
    bool _on;

    public ToggleSwitch(bool on, Action<bool>? changed)
    {
        _on = on;
        _changed = changed;
        Width = 44;
        Height = 26;
        CornerRadius = new CornerRadius(13);
        Cursor = Cursors.Hand;
        _knob = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        Child = _knob;
        PaintKnob();
        MouseLeftButtonUp += (_, _) =>
        {
            _on = !_on;
            PaintKnob();
            _changed?.Invoke(_on);
        };
    }

    public void SetSilent(bool on)
    {
        _on = on;
        PaintKnob();
    }

    void PaintKnob()
    {
        Background = Paint.Brush(_on ? Paint.Accent : Color.FromRgb(0xE4, 0xD9, 0xE0));
        _knob.HorizontalAlignment = _on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _knob.Margin = new Thickness(3);
    }
}
