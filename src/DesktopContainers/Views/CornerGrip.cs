using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DesktopContainers;

public sealed class CornerGrip : Grid
{
    readonly Border _longH;
    readonly Border _longV;
    readonly Border _shortH;
    readonly Border _shortV;

    public CornerGrip()
    {
        Width = 28;
        Height = 28;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(0, 0, 6, 6);
        Cursor = System.Windows.Input.Cursors.SizeNWSE;
        Background = Brushes.Transparent;
        IsHitTestVisible = true;
        Children.Add(new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(7),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 1, 1),
            Background = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false
        });

        _longH = Bar(11, 2, HorizontalAlignment.Right, VerticalAlignment.Bottom, new Thickness(0, 0, 4, 5));
        _longV = Bar(2, 11, HorizontalAlignment.Right, VerticalAlignment.Bottom, new Thickness(0, 0, 5, 4));
        _shortH = Bar(6, 2, HorizontalAlignment.Right, VerticalAlignment.Bottom, new Thickness(0, 0, 8, 9));
        _shortV = Bar(2, 6, HorizontalAlignment.Right, VerticalAlignment.Bottom, new Thickness(0, 0, 9, 8));
        Children.Add(_longH);
        Children.Add(_longV);
        Children.Add(_shortH);
        Children.Add(_shortV);
        Paint(false, false);

        MouseEnter += (_, _) => Paint(true, true);
        MouseLeave += (_, _) => Paint(false, true);
    }

    static Border Bar(double width, double height, HorizontalAlignment x, VerticalAlignment y, Thickness margin)
    {
        return new Border
        {
            Width = width,
            Height = height,
            HorizontalAlignment = x,
            VerticalAlignment = y,
            Margin = margin,
            CornerRadius = new CornerRadius(1),
            IsHitTestVisible = false
        };
    }

    void Paint(bool hot, bool animate)
    {
        var strong = Color.FromArgb(hot ? (byte)230 : (byte)150, 255, 255, 255);
        var soft = Color.FromArgb(hot ? (byte)160 : (byte)90, 255, 255, 255);
        Animate(_longH, strong, animate);
        Animate(_longV, strong, animate);
        Animate(_shortH, soft, animate);
        Animate(_shortV, soft, animate);
    }

    static void Animate(Border bar, Color color, bool animate)
    {
        var brush = bar.Background as SolidColorBrush;
        if (brush == null || brush.IsFrozen)
        {
            brush = new SolidColorBrush(color);
            bar.Background = brush;
        }
        if (!animate)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.Color = color;
            return;
        }
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(color, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        });
    }
}
