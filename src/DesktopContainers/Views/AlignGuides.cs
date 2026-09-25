using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DesktopContainers;

public static class AlignGuides
{
    public const double Frame = 16;
    const double Snap = 8;
    const double Exact = 0.8;
    const double MinGap = 12;

    public readonly record struct Box(double Left, double Top, double Right, double Bottom)
    {
        public double Width => Right - Left;
        public double Height => Bottom - Top;
        public double CenterX => (Left + Right) / 2;
        public double CenterY => (Top + Bottom) / 2;
        public Box Shift(double dx, double dy) => new(Left + dx, Top + dy, Right + dx, Bottom + dy);
    }

    public readonly record struct Line(bool Vertical, double Position, double Start, double End, bool Spacing);

    static Layer? _layer;

    public static Box FromWindow(Window window)
    {
        var width = window.ActualWidth > 1 ? window.ActualWidth : window.Width;
        var height = window.ActualHeight > 1 ? window.ActualHeight : window.Height;
        return new Box(window.Left + Frame, window.Top + Frame, window.Left + width - Frame, window.Top + height - Frame);
    }

    public static (double Left, double Top, IReadOnlyList<Line> Lines) Place(Box raw, IReadOnlyList<Box> others)
    {
        if (others.Count == 0 || raw.Width < 24 || raw.Height < 24)
            return (raw.Left - Frame, raw.Top - Frame, Array.Empty<Line>());

        var dx = 0d;
        var dy = 0d;
        var bestX = Snap + 1;
        var bestY = Snap + 1;
        var edgeX = false;
        var edgeY = false;

        void OfferX(double delta, bool edge)
        {
            var abs = Math.Abs(delta);
            if (abs > Snap) return;
            if (abs < bestX - 0.15 || (abs <= bestX + 0.15 && edge && !edgeX))
            {
                bestX = abs;
                dx = delta;
                edgeX = edge;
            }
        }

        void OfferY(double delta, bool edge)
        {
            var abs = Math.Abs(delta);
            if (abs > Snap) return;
            if (abs < bestY - 0.15 || (abs <= bestY + 0.15 && edge && !edgeY))
            {
                bestY = abs;
                dy = delta;
                edgeY = edge;
            }
        }

        foreach (var other in others)
        {
            foreach (var mine in new[] { raw.Left, raw.CenterX, raw.Right })
            foreach (var theirs in new[] { other.Left, other.CenterX, other.Right })
                OfferX(theirs - mine, true);
            foreach (var mine in new[] { raw.Top, raw.CenterY, raw.Bottom })
            foreach (var theirs in new[] { other.Top, other.CenterY, other.Bottom })
                OfferY(theirs - mine, true);
        }

        OfferSpacing(raw, others, horizontal: true, OfferX);
        OfferSpacing(raw, others, horizontal: false, OfferY);

        var snapped = raw.Shift(dx, dy);
        var lines = new List<Line>();
        CollectAlign(snapped, others, lines);
        CollectSpacing(snapped, others, lines);
        return (snapped.Left - Frame, snapped.Top - Frame, lines);
    }

    public static void Show(IReadOnlyList<Line> lines)
    {
        if (lines.Count == 0)
        {
            Hide();
            return;
        }
        _layer ??= new Layer();
        _layer.Update(lines);
    }

    public static void Hide() => _layer?.Clear();

    static void OfferSpacing(Box raw, IReadOnlyList<Box> others, bool horizontal, Action<double, bool> offer)
    {
        var gaps = AdjacentGaps(others, horizontal);
        if (horizontal)
        {
            Box? left = null;
            Box? right = null;
            foreach (var other in others)
            {
                if (!Overlap(raw.Top, raw.Bottom, other.Top, other.Bottom)) continue;
                if (other.Right <= raw.Left + Snap && (left == null || other.Right > left.Value.Right))
                    left = other;
                if (other.Left >= raw.Right - Snap && (right == null || other.Left < right.Value.Left))
                    right = other;
            }
            if (left is { } nearLeft && right is { } nearRight && nearRight.Left - nearLeft.Right >= raw.Width + MinGap)
                offer((nearLeft.Right + nearRight.Left - raw.Width) / 2 - raw.Left, false);

            foreach (var gap in gaps)
            foreach (var other in others)
            {
                if (!Overlap(raw.Top, raw.Bottom, other.Top, other.Bottom)) continue;
                offer(other.Right + gap.Size - raw.Left, false);
                offer(other.Left - gap.Size - raw.Width - raw.Left, false);
            }
            return;
        }

        Box? above = null;
        Box? below = null;
        foreach (var other in others)
        {
            if (!Overlap(raw.Left, raw.Right, other.Left, other.Right)) continue;
            if (other.Bottom <= raw.Top + Snap && (above == null || other.Bottom > above.Value.Bottom))
                above = other;
            if (other.Top >= raw.Bottom - Snap && (below == null || other.Top < below.Value.Top))
                below = other;
        }
        if (above is { } nearAbove && below is { } nearBelow && nearBelow.Top - nearAbove.Bottom >= raw.Height + MinGap)
            offer((nearAbove.Bottom + nearBelow.Top - raw.Height) / 2 - raw.Top, false);

        foreach (var gap in gaps)
        foreach (var other in others)
        {
            if (!Overlap(raw.Left, raw.Right, other.Left, other.Right)) continue;
            offer(other.Bottom + gap.Size - raw.Top, false);
            offer(other.Top - gap.Size - raw.Height - raw.Top, false);
        }
    }

    static void CollectAlign(Box moving, IReadOnlyList<Box> others, List<Line> lines)
    {
        var all = new List<Box>(others) { moving };
        void Vertical(double x)
        {
            if (!others.Any(other => Near(other.Left, x) || Near(other.Right, x) || Near(other.CenterX, x))) return;
            var hits = all.Where(other => Near(other.Left, x) || Near(other.Right, x) || Near(other.CenterX, x)).ToList();
            if (hits.Count < 2) return;
            Add(lines, new Line(true, x, hits.Min(other => other.Top) - 14, hits.Max(other => other.Bottom) + 14, false));
        }
        void Horizontal(double y)
        {
            if (!others.Any(other => Near(other.Top, y) || Near(other.Bottom, y) || Near(other.CenterY, y))) return;
            var hits = all.Where(other => Near(other.Top, y) || Near(other.Bottom, y) || Near(other.CenterY, y)).ToList();
            if (hits.Count < 2) return;
            Add(lines, new Line(false, y, hits.Min(other => other.Left) - 14, hits.Max(other => other.Right) + 14, false));
        }
        Vertical(moving.Left);
        Vertical(moving.Right);
        Vertical(moving.CenterX);
        Horizontal(moving.Top);
        Horizontal(moving.Bottom);
        Horizontal(moving.CenterY);
    }

    static void CollectSpacing(Box moving, IReadOnlyList<Box> others, List<Line> lines)
    {
        var all = new List<Box>(others) { moving };
        DrawMatched(AdjacentGaps(all, true), moving, true, lines);
        DrawMatched(AdjacentGaps(all, false), moving, false, lines);
    }

    static void DrawMatched(List<Gap> gaps, Box moving, bool horizontal, List<Line> lines)
    {
        foreach (var gap in gaps)
        {
            if (!Touches(gap, moving)) continue;
            if (!gaps.Any(other => !Same(other, gap) && Math.Abs(other.Size - gap.Size) <= 1.2)) continue;
            Add(lines, SpaceLine(gap, horizontal));
            var reference = gaps
                .Where(other => !Touches(other, moving) && Math.Abs(other.Size - gap.Size) <= 1.2)
                .OrderBy(other => Math.Abs(other.First.CenterX - gap.First.CenterX) + Math.Abs(other.First.CenterY - gap.First.CenterY))
                .FirstOrDefault();
            if (reference.Size >= MinGap)
                Add(lines, SpaceLine(reference, horizontal));
        }
    }

    static Line SpaceLine(Gap gap, bool horizontal)
    {
        if (horizontal)
        {
            var top = Math.Max(gap.First.Top, gap.Second.Top);
            var bottom = Math.Min(gap.First.Bottom, gap.Second.Bottom);
            return new Line(false, (top + bottom) / 2, gap.First.Right, gap.Second.Left, true);
        }
        var left = Math.Max(gap.First.Left, gap.Second.Left);
        var right = Math.Min(gap.First.Right, gap.Second.Right);
        return new Line(true, (left + right) / 2, gap.First.Bottom, gap.Second.Top, true);
    }

    static List<Gap> AdjacentGaps(IReadOnlyList<Box> boxes, bool horizontal)
    {
        var list = new List<Gap>();
        for (var i = 0; i < boxes.Count; i++)
        for (var j = 0; j < boxes.Count; j++)
        {
            if (i == j) continue;
            var first = boxes[i];
            var second = boxes[j];
            if (horizontal)
            {
                var gap = second.Left - first.Right;
                if (gap < MinGap || !Overlap(first.Top, first.Bottom, second.Top, second.Bottom)) continue;
                if (boxes.Any(box => box.CenterX > first.Right && box.CenterX < second.Left && Overlap(box.Top, box.Bottom, first.Top, first.Bottom)))
                    continue;
                list.Add(new Gap(first, second, gap));
                continue;
            }
            var vertical = second.Top - first.Bottom;
            if (vertical < MinGap || !Overlap(first.Left, first.Right, second.Left, second.Right)) continue;
            if (boxes.Any(box => box.CenterY > first.Bottom && box.CenterY < second.Top && Overlap(box.Left, box.Right, first.Left, first.Right)))
                continue;
            list.Add(new Gap(first, second, vertical));
        }
        return list;
    }

    static bool Overlap(double a1, double a2, double b1, double b2) => Math.Min(a2, b2) - Math.Max(a1, b1) > 8;

    static bool Near(double a, double b) => Math.Abs(a - b) <= Exact;

    static bool Touches(Gap gap, Box moving) => gap.First.Equals(moving) || gap.Second.Equals(moving);

    static bool Same(Gap a, Gap b) =>
        (a.First.Equals(b.First) && a.Second.Equals(b.Second)) ||
        (a.First.Equals(b.Second) && a.Second.Equals(b.First));

    static void Add(List<Line> lines, Line line)
    {
        if (Math.Abs(line.End - line.Start) < 4) return;
        if (lines.Any(other => other.Vertical == line.Vertical && other.Spacing == line.Spacing &&
                               Math.Abs(other.Position - line.Position) < 0.6 &&
                               Math.Abs(other.Start - line.Start) < 0.6 &&
                               Math.Abs(other.End - line.End) < 0.6))
            return;
        lines.Add(line);
    }

    readonly record struct Gap(Box First, Box Second, double Size);

    sealed class Layer : Window
    {
        IReadOnlyList<Line> _lines = Array.Empty<Line>();

        public Layer()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;
            IsHitTestVisible = false;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
                style |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TRANSPARENT;
                NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style));
            };
        }

        public void Update(IReadOnlyList<Line> lines)
        {
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = Math.Max(1, SystemParameters.VirtualScreenWidth);
            Height = Math.Max(1, SystemParameters.VirtualScreenHeight);
            _lines = lines;
            if (!IsVisible) Show();
            InvalidateVisual();
        }

        public void Clear()
        {
            _lines = Array.Empty<Line>();
            if (IsVisible) Hide();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var haloBrush = new SolidColorBrush(Color.FromArgb(96, 24, 8, 16));
            var inkBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x98));
            haloBrush.Freeze();
            inkBrush.Freeze();
            var halo = new Pen(haloBrush, 3.4);
            var ink = new Pen(inkBrush, 1.5)
            {
                DashStyle = new DashStyle(new double[] { 5, 4 }, 0),
                DashCap = PenLineCap.Flat
            };
            halo.Freeze();
            ink.Freeze();
            var originX = Left;
            var originY = Top;
            foreach (var line in _lines)
            {
                if (line.Vertical)
                {
                    var x = line.Position - originX;
                    var y1 = Math.Min(line.Start, line.End) - originY;
                    var y2 = Math.Max(line.Start, line.End) - originY;
                    Draw(dc, halo, ink, new Point(x, y1), new Point(x, y2));
                    if (!line.Spacing) continue;
                    Draw(dc, halo, ink, new Point(x - 6, y1), new Point(x + 6, y1));
                    Draw(dc, halo, ink, new Point(x - 6, y2), new Point(x + 6, y2));
                    continue;
                }
                var y = line.Position - originY;
                var x1 = Math.Min(line.Start, line.End) - originX;
                var x2 = Math.Max(line.Start, line.End) - originX;
                Draw(dc, halo, ink, new Point(x1, y), new Point(x2, y));
                if (!line.Spacing) continue;
                Draw(dc, halo, ink, new Point(x1, y - 6), new Point(x1, y + 6));
                Draw(dc, halo, ink, new Point(x2, y - 6), new Point(x2, y + 6));
            }
        }

        static void Draw(DrawingContext dc, Pen halo, Pen ink, Point a, Point b)
        {
            dc.DrawLine(halo, a, b);
            dc.DrawLine(ink, a, b);
        }
    }
}
