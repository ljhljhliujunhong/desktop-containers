using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DesktopContainers;

public static class Motion
{
    public static void Number(DependencyObject target, DependencyProperty property, double to, int milliseconds, bool enabled)
    {
        var animatable = (IAnimatable)target;
        if (!enabled)
        {
            animatable.BeginAnimation(property, null);
            target.SetValue(property, to);
            return;
        }

        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        animation.Completed += (_, _) =>
        {
            animatable.BeginAnimation(property, null);
            target.SetValue(property, to);
        };
        animatable.BeginAnimation(property, animation);
    }

    public static void Fade(UIElement element, double to, bool enabled, int milliseconds = 180) =>
        Number(element, UIElement.OpacityProperty, to, milliseconds, enabled);

    public static void Scale(ScaleTransform transform, double to, bool enabled)
    {
        Number(transform, ScaleTransform.ScaleXProperty, to, 140, enabled);
        Number(transform, ScaleTransform.ScaleYProperty, to, 140, enabled);
    }
}
