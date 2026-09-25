using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DesktopContainers;

public sealed class GlassSurface : Grid
{
    readonly Border _sheen;
    readonly Border _inner;
    readonly Border _glint;
    readonly GradientStop _lead;
    readonly GradientStop _peak;
    readonly GradientStop _tail;
    readonly GradientStop _glintLead;
    readonly GradientStop _glintPeak;
    readonly GradientStop _glintTail;
    readonly Dictionary<UIElement, AnimationTimeline> _fades = new();
    Storyboard? _sweep;
    bool _hot;
    bool _edgeReady;
    Color _edge;
    int _glintGen;

    public LinearGradientBrush Rim { get; }

    public GlassSurface()
    {
        IsHitTestVisible = false;
        _sheen = new Border
        {
            IsHitTestVisible = false,
            Opacity = 0.42,
            Background = SheenBrush()
        };
        _inner = new Border
        {
            IsHitTestVisible = false,
            Opacity = 0.34,
            BorderThickness = new Thickness(1),
            BorderBrush = Paint.Brush(Color.FromArgb(150, 255, 255, 255)),
            Background = Brushes.Transparent
        };
        _glintLead = new GradientStop(Color.FromArgb(0, 255, 255, 255), 0);
        _glintPeak = new GradientStop(Color.FromArgb(210, 255, 255, 255), 0.5);
        _glintTail = new GradientStop(Color.FromArgb(0, 255, 255, 255), 1);
        var glintBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            MappingMode = BrushMappingMode.RelativeToBoundingBox
        };
        glintBrush.GradientStops.Add(_glintLead);
        glintBrush.GradientStops.Add(_glintPeak);
        glintBrush.GradientStops.Add(_glintTail);
        _glint = new Border
        {
            IsHitTestVisible = false,
            Opacity = 0,
            BorderThickness = new Thickness(1.5),
            BorderBrush = glintBrush,
            Background = Brushes.Transparent
        };
        Children.Add(_sheen);
        Children.Add(_inner);
        Children.Add(_glint);

        _lead = new GradientStop(Colors.White, 0);
        _peak = new GradientStop(Colors.White, 0.42);
        _tail = new GradientStop(Colors.White, 1);
        Rim = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            MappingMode = BrushMappingMode.RelativeToBoundingBox
        };
        Rim.GradientStops.Add(_lead);
        Rim.GradientStops.Add(_peak);
        Rim.GradientStops.Add(_tail);
        Rest(Colors.White);
    }

    public void Sync(Appearance appearance, bool hot, bool animate)
    {
        var radius = Math.Max(4, appearance.CornerRadius - 2);
        _sheen.CornerRadius = new CornerRadius(radius);
        _inner.CornerRadius = new CornerRadius(radius);
        _glint.CornerRadius = new CornerRadius(radius);
        var edge = Edge(appearance);
        if (!_edgeReady || edge != _edge)
        {
            _edge = edge;
            _edgeReady = true;
            Rest(edge);
        }

        if (hot == _hot) return;
        _hot = hot;
        if (hot) FadeIn(animate);
        else FadeOut(animate);
    }

    public void Stop() => StopSweep();

    public static Brush StaticRim(Appearance appearance)
    {
        var edge = Edge(appearance);
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(220, 255, 255, 255), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(70, edge.R, edge.G, edge.B), 0.46));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(180, 255, 255, 255), 1));
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }

    static LinearGradientBrush SheenBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.15, 0),
            EndPoint = new Point(0.85, 1)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x66, 255, 255, 255), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x22, 255, 255, 255), 0.18));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 255, 255, 255), 0.46));
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }

    static Color Edge(Appearance appearance)
    {
        var tint = Paint.Hex(appearance.BorderColor);
        return Color.FromRgb(
            (byte)Math.Min(255, tint.R * 0.28 + 196),
            (byte)Math.Min(255, tint.G * 0.28 + 196),
            (byte)Math.Min(255, tint.B * 0.28 + 210));
    }

    void Rest(Color edge)
    {
        _lead.Color = Color.FromArgb(230, 255, 255, 255);
        _lead.Offset = 0;
        _peak.Color = Color.FromArgb(64, edge.R, edge.G, edge.B);
        _peak.Offset = 0.42;
        _tail.Color = Color.FromArgb(176, 255, 255, 255);
        _tail.Offset = 1;
    }

    void FadeIn(bool animate)
    {
        ++_glintGen;
        FadeOpacity(_sheen, 0.72, animate, 860, null);
        FadeOpacity(_inner, 0.72, animate, 980, null);
        FadeOpacity(_glint, 0.92, animate, 920, null);
        if (!animate)
        {
            PlaceGlint(0.34);
            return;
        }
        StartSweep();
    }

    void FadeOut(bool animate)
    {
        FadeOpacity(_sheen, 0.42, animate, 900, null);
        FadeOpacity(_inner, 0.34, animate, 900, null);
        var gen = ++_glintGen;
        FadeOpacity(_glint, 0, animate, 980, () =>
        {
            if (gen != _glintGen || _hot) return;
            StopSweep();
        });
    }

    void StartSweep()
    {
        _sweep?.Stop();
        ClearGlint();
        var storyboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        AddSweep(storyboard, _glintLead, -0.35, 0.72);
        AddSweep(storyboard, _glintPeak, -0.12, 0.95);
        AddSweep(storyboard, _glintTail, 0.12, 1.18);
        _sweep = storyboard;
        try
        {
            storyboard.Begin();
        }
        catch (Exception ex)
        {
            Log.Error("glass", ex);
            _sweep = null;
            PlaceGlint(0.34);
        }
    }

    void StopSweep()
    {
        _sweep?.Stop();
        _sweep = null;
        ClearGlint();
        _glintLead.Offset = 0;
        _glintPeak.Offset = 0.5;
        _glintTail.Offset = 1;
    }

    static void AddSweep(Storyboard storyboard, GradientStop stop, double from, double to)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(2600))
        {
            AutoReverse = true,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            RepeatBehavior = RepeatBehavior.Forever
        };
        Storyboard.SetTarget(animation, stop);
        Storyboard.SetTargetProperty(animation, new PropertyPath(GradientStop.OffsetProperty));
        storyboard.Children.Add(animation);
    }

    void FadeOpacity(UIElement element, double to, bool animate, int milliseconds, Action? done)
    {
        if (!animate)
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = to;
            done?.Invoke();
            return;
        }

        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        _fades[element] = animation;
        animation.Completed += (_, _) =>
        {
            if (_fades.TryGetValue(element, out var current) && current != animation) return;
            _fades.Remove(element);
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = to;
            done?.Invoke();
        };
        element.BeginAnimation(OpacityProperty, animation);
    }

    void PlaceGlint(double peak)
    {
        _glintLead.Offset = peak - 0.2;
        _glintPeak.Offset = peak;
        _glintTail.Offset = peak + 0.2;
    }

    void ClearGlint()
    {
        _glintLead.BeginAnimation(GradientStop.OffsetProperty, null);
        _glintPeak.BeginAnimation(GradientStop.OffsetProperty, null);
        _glintTail.BeginAnimation(GradientStop.OffsetProperty, null);
    }
}
