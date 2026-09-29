namespace DesktopContainers;

/// <summary>
/// 把容器的像素矩形排进当前工作区。纯计算，不碰窗口。
/// 已经分开并且都在屏幕里的保持原位；从别的显示器掉下来的再找空位。
/// </summary>
public static class DisplayReflow
{
    const int Margin = 16;
    const int Gap = 8;
    const double InsideRatio = 0.82;
    const double StayOverlap = 0.22;
    const int MinWide = 160;
    const int MinTall = 120;
    const int CascadeX = 64;
    const int CascadeY = 48;

    public static PixelRect[] Fit(PixelRect[] anchors, PixelRect[] workAreas, bool allowShrink = true)
    {
        if (anchors.Length == 0) return [];
        var works = new List<PixelRect>(workAreas.Length);
        foreach (var work in workAreas)
        {
            if (work.W > 0 && work.H > 0)
                works.Add(work);
        }
        if (works.Count == 0) return anchors.ToArray();

        if (allowShrink && AllMostlyInside(anchors, works) && !HasHeavyOverlap(anchors, StayOverlap))
            return anchors.ToArray();

        if (allowShrink && works.Count == 1 && !HasHeavyOverlap(anchors, StayOverlap)
            && TryProportional(anchors, works[0], out var scaled))
            return scaled;

        var order = Order(anchors);
        var stay = MarkStays(anchors, works, order);
        if (allowShrink)
        {
            for (var percent = 100; percent >= 45; percent -= 5)
            {
                if (TryPlaceAll(anchors, stay, order, works, percent / 100.0, true, out var placed))
                    return placed;
            }
        }
        else if (TryPlaceAll(anchors, stay, order, works, 1, false, out var exact))
        {
            return exact;
        }

        return Cascade(anchors, stay, order, works);
    }

    public static bool MostlyInside(PixelRect rect, IReadOnlyList<PixelRect> workAreas)
    {
        if (rect.Area <= 0) return false;
        long covered = 0;
        foreach (var work in workAreas)
        {
            if (work.W <= 0 || work.H <= 0) continue;
            covered += PixelRect.IntersectionArea(rect, work);
        }
        if (covered > rect.Area) covered = rect.Area;
        return covered / (double)rect.Area >= InsideRatio;
    }

    public static bool HasHeavyOverlap(IReadOnlyList<PixelRect> rects, double threshold)
    {
        for (var i = 0; i < rects.Count; i++)
        {
            for (var j = i + 1; j < rects.Count; j++)
            {
                if (OverlapRatio(rects[i], rects[j]) > threshold)
                    return true;
            }
        }
        return false;
    }

    public static void Check()
    {
        var hd = new[] { new PixelRect(0, 0, 1920, 1080) };
        var pair = new[]
        {
            new PixelRect(40, 40, 300, 220),
            new PixelRect(420, 40, 300, 220)
        };
        var identity = Fit(pair, hd);
        Require(Same(identity, pair), "分开的两只应留在原地", identity);

        var piled = new[]
        {
            new PixelRect(100, 100, 400, 300),
            new PixelRect(100, 100, 400, 300),
            new PixelRect(100, 100, 400, 300)
        };
        var spread = Fit(piled, hd);
        Require(spread[0] == piled[0] && AllInside(spread, hd[0]) && NoOverlap(spread), "叠在一起的要散开，第一只留在原地", spread);

        var fallen = new[]
        {
            new PixelRect(3000, 40, 420, 300),
            new PixelRect(3000, 40, 420, 300),
            new PixelRect(3000, 40, 420, 300)
        };
        var brought = Fit(fallen, hd);
        Require(AllInside(brought, hd[0]) && NoOverlap(brought) && brought.All(rect => rect.X != 3000), "屏幕外的要搬回来", brought);

        var home = new PixelRect(80, 80, 360, 280);
        var mixed = Fit([home, new PixelRect(4000, 80, 360, 280)], hd);
        Require(mixed[0] == home && AllInside(mixed, hd[0]) && NoOverlap(mixed), "还在屏幕里的不要被带着走", mixed);

        var wide = Fit([new PixelRect(0, 0, 3000, 400)], hd);
        Require(wide.Length == 1 && wide[0].W <= 1920 - Margin * 2 && InsideInset(wide[0], hd[0]), "超宽的要缩进工作区", wide);

        var small = new PixelRect(0, 0, 800, 600);
        var scaled = Fit(
        [
            new PixelRect(20, 20, 300, 200),
            new PixelRect(600, 20, 300, 200)
        ], [small]);
        Require(
            scaled.Length == 2
            && InsideInset(scaled[0], small)
            && InsideInset(scaled[1], small)
            && PixelRect.IntersectionArea(scaled[0], scaled[1]) == 0
            && scaled[1].X > scaled[0].X
            && scaled[1].X < 600
            && scaled[0].W is >= 250 and <= 275
            && scaled[1].W is >= 250 and <= 275,
            "同一块屏幕变小后应一起缩小",
            scaled);

        var dual = new[]
        {
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(1920, 0, 1920, 1080)
        };
        var across = new[]
        {
            new PixelRect(100, 80, 400, 280),
            new PixelRect(2100, 80, 400, 280)
        };
        Require(Same(Fit(across, dual), across), "两台显示器上都放得下的应留在原地", across);

        var laptop = new PixelRect(0, 0, 1366, 768);
        var crowd = new PixelRect[8];
        for (var i = 0; i < crowd.Length; i++)
            crowd[i] = new PixelRect(4000, 40, 700, 500);
        var packed = Fit(crowd, [laptop]);
        Require(packed.All(rect => InsideInset(rect, laptop)) && AllSeparated(packed), "小屏幕上八只要彼此分开", packed);

        var left = new[]
        {
            new PixelRect(-1920, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1080)
        };
        var sided = new[]
        {
            new PixelRect(-1600, 80, 420, 300),
            new PixelRect(120, 80, 420, 300)
        };
        Require(Same(Fit(sided, left), sided), "左侧显示器上的位置应留住", sided);

        var almost = new PixelRect(16, 16, 700, 500);
        var edge = Fit([almost, new PixelRect(750, 16, 100, 80)], [small]);
        Require(edge[0] == almost && InsideInset(edge[0], small) && InsideInset(edge[1], small) && Separated(edge[0], edge[1]), "只露出一半的不要把旁边那只一起缩小", edge);
    }

    static bool TryProportional(PixelRect[] anchors, PixelRect work, out PixelRect[] result)
    {
        result = anchors;
        var innerW = work.W - Margin * 2;
        var innerH = work.H - Margin * 2;
        if (innerW < 80 || innerH < 80) return false;
        var bounds = BoundsOf(anchors);
        if (bounds.W <= 0 || bounds.H <= 0) return false;
        var cx = bounds.X + bounds.W / 2.0;
        var cy = bounds.Y + bounds.H / 2.0;
        if (cx < work.X || cx >= work.Right || cy < work.Y || cy >= work.Bottom) return false;
        var scale = Math.Min(1.0, Math.Min(innerW / (double)bounds.W, innerH / (double)bounds.H));
        if (scale < 0.45 || scale > 0.9) return false;

        for (var attempt = 0; attempt <= 4; attempt++)
        {
            var factor = scale * (1 - 0.004 * attempt);
            if (factor <= 0) continue;
            var mapped = new PixelRect[anchors.Length];
            var ok = true;
            for (var i = 0; i < anchors.Length; i++)
            {
                var source = anchors[i];
                var width = Math.Max(1, Round(source.W * factor));
                var height = Math.Max(1, Round(source.H * factor));
                var x = work.X + Margin + Round((source.X - bounds.X) * factor);
                var y = work.Y + Margin + Round((source.Y - bounds.Y) * factor);
                var left = work.X + Margin;
                var top = work.Y + Margin;
                var right = work.Right - Margin;
                var bottom = work.Bottom - Margin;
                if (x + width > right) x = right - width;
                if (y + height > bottom) y = bottom - height;
                if (x < left) x = left;
                if (y < top) y = top;
                var rect = new PixelRect(x, y, width, height);
                if (!InsideInset(rect, work))
                {
                    ok = false;
                    break;
                }
                mapped[i] = rect;
            }
            if (!ok || !NoOverlap(mapped)) continue;
            result = mapped;
            return true;
        }
        return false;
    }

    static bool TryPlaceAll(
        PixelRect[] anchors,
        bool[] stay,
        int[] order,
        List<PixelRect> works,
        double scale,
        bool allowCap,
        out PixelRect[] result)
    {
        result = anchors.ToArray();
        var occupied = new List<PixelRect>();
        foreach (var index in order)
        {
            if (stay[index]) occupied.Add(anchors[index]);
        }

        foreach (var index in order)
        {
            if (stay[index]) continue;
            ScaledSize(anchors[index], scale, out var width, out var height);
            if (TryPlace(width, height, occupied, works, out var placed))
            {
                result[index] = placed;
                occupied.Add(placed);
                continue;
            }
            if (!allowCap || FitsSomeWork(width, height, works))
                return false;
            var host = Largest(works);
            var cappedW = Math.Min(width, host.W - Margin * 2);
            var cappedH = Math.Min(height, host.H - Margin * 2);
            if (cappedW < 1 || cappedH < 1 || (cappedW == width && cappedH == height))
                return false;
            if (!TryPlace(cappedW, cappedH, occupied, works, out placed))
                return false;
            result[index] = placed;
            occupied.Add(placed);
        }
        return true;
    }

    static bool TryPlace(int width, int height, List<PixelRect> occupied, List<PixelRect> works, out PixelRect placed)
    {
        placed = default;
        if (width < 1 || height < 1) return false;
        var candidates = new List<(int X, int Y)>();
        var seen = new HashSet<(int X, int Y)>();
        foreach (var work in works.OrderBy(rect => rect.Y).ThenBy(rect => rect.X))
        {
            var left = work.X + Margin;
            var top = work.Y + Margin;
            var right = work.Right - Margin;
            var bottom = work.Bottom - Margin;
            if (width > right - left || height > bottom - top) continue;
            Add(left, top);
            for (var y = top; y + height <= bottom; y += 16)
            {
                for (var x = left; x + width <= right; x += 16)
                    Add(x, y);
            }
        }
        foreach (var item in occupied)
        {
            Add(item.Right + Gap, item.Y);
            Add(item.X, item.Bottom + Gap);
        }

        candidates.Sort(static (a, b) =>
        {
            var byY = a.Y.CompareTo(b.Y);
            return byY != 0 ? byY : a.X.CompareTo(b.X);
        });
        foreach (var (x, y) in candidates)
        {
            var rect = new PixelRect(x, y, width, height);
            if (!works.Any(work => InsideInset(rect, work))) continue;
            var clear = true;
            foreach (var item in occupied)
            {
                if (Separated(rect, item)) continue;
                clear = false;
                break;
            }
            if (!clear) continue;
            placed = rect;
            return true;
        }
        return false;

        void Add(int x, int y)
        {
            if (seen.Add((x, y))) candidates.Add((x, y));
        }
    }

    static PixelRect[] Cascade(PixelRect[] anchors, bool[] stay, int[] order, List<PixelRect> works)
    {
        var result = anchors.ToArray();
        var work = Largest(works);
        var limitX = work.X + Math.Max(0, work.W - 80);
        var limitY = work.Y + Math.Max(0, work.H - 80);
        var movers = order.Where(index => !stay[index]).ToArray();
        var spanX = Math.Max(0, limitX - work.X);
        var spanY = Math.Max(0, limitY - work.Y);
        var cols = Math.Max(1, spanX / CascadeX + 1);
        var rows = Math.Max(1, spanY / CascadeY + 1);
        var stepX = CascadeX;
        var stepY = CascadeY;
        if (movers.Length > cols * rows)
        {
            cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(movers.Length)));
            rows = Math.Max(1, (movers.Length + cols - 1) / cols);
            stepX = cols == 1 ? 0 : Math.Max(1, spanX / (cols - 1));
            stepY = rows == 1 ? 0 : Math.Max(1, spanY / (rows - 1));
        }
        for (var n = 0; n < movers.Length; n++)
        {
            var x = work.X + (n % cols) * stepX;
            var y = work.Y + (n / cols) * stepY;
            if (x > limitX) x = limitX;
            if (y > limitY) y = limitY;
            var index = movers[n];
            result[index] = new PixelRect(x, y, Math.Max(1, anchors[index].W), Math.Max(1, anchors[index].H));
        }
        return result;
    }

    static bool[] MarkStays(PixelRect[] anchors, List<PixelRect> works, int[] order)
    {
        var stay = new bool[anchors.Length];
        var kept = new List<int>();
        foreach (var index in order)
        {
            if (!MostlyInside(anchors[index], works)) continue;
            var heavy = false;
            foreach (var other in kept)
            {
                if (OverlapRatio(anchors[index], anchors[other]) <= StayOverlap) continue;
                heavy = true;
                break;
            }
            if (heavy) continue;
            stay[index] = true;
            kept.Add(index);
        }
        return stay;
    }

    static void ScaledSize(PixelRect source, double scale, out int width, out int height)
    {
        width = Math.Max(1, Round(source.W * scale));
        height = Math.Max(1, Round(source.H * scale));
        if (source.W >= MinWide) width = Math.Max(width, MinWide);
        if (source.H >= MinTall) height = Math.Max(height, MinTall);
    }

    static bool FitsSomeWork(int width, int height, List<PixelRect> works)
    {
        foreach (var work in works)
        {
            if (width <= work.W - Margin * 2 && height <= work.H - Margin * 2)
                return true;
        }
        return false;
    }

    static bool AllMostlyInside(PixelRect[] anchors, List<PixelRect> works)
    {
        foreach (var anchor in anchors)
        {
            if (!MostlyInside(anchor, works)) return false;
        }
        return true;
    }

    static int[] Order(PixelRect[] anchors)
    {
        return Enumerable.Range(0, anchors.Length)
            .OrderBy(index => anchors[index].Y)
            .ThenBy(index => anchors[index].X)
            .ThenBy(index => index)
            .ToArray();
    }

    static PixelRect BoundsOf(PixelRect[] rects)
    {
        var left = int.MaxValue;
        var top = int.MaxValue;
        var right = int.MinValue;
        var bottom = int.MinValue;
        foreach (var rect in rects)
        {
            if (rect.X < left) left = rect.X;
            if (rect.Y < top) top = rect.Y;
            if (rect.Right > right) right = rect.Right;
            if (rect.Bottom > bottom) bottom = rect.Bottom;
        }
        return new PixelRect(left, top, right - left, bottom - top);
    }

    static PixelRect Largest(List<PixelRect> works) =>
        works.OrderByDescending(rect => rect.Area).ThenBy(rect => rect.Y).ThenBy(rect => rect.X).First();

    static bool InsideInset(PixelRect rect, PixelRect work)
    {
        if (work.W <= 0 || work.H <= 0 || rect.W <= 0 || rect.H <= 0) return false;
        return rect.X >= work.X + Margin
            && rect.Y >= work.Y + Margin
            && rect.Right <= work.Right - Margin
            && rect.Bottom <= work.Bottom - Margin;
    }

    static bool Separated(PixelRect a, PixelRect b) =>
        a.Right + Gap <= b.X || b.Right + Gap <= a.X || a.Bottom + Gap <= b.Y || b.Bottom + Gap <= a.Y;

    static bool AllSeparated(PixelRect[] rects)
    {
        for (var i = 0; i < rects.Length; i++)
        {
            for (var j = i + 1; j < rects.Length; j++)
            {
                if (!Separated(rects[i], rects[j])) return false;
            }
        }
        return true;
    }

    static bool AllInside(PixelRect[] rects, PixelRect work)
    {
        foreach (var rect in rects)
        {
            if (rect.X < work.X || rect.Y < work.Y || rect.Right > work.Right || rect.Bottom > work.Bottom)
                return false;
        }
        return true;
    }

    static bool NoOverlap(PixelRect[] rects)
    {
        for (var i = 0; i < rects.Length; i++)
        {
            for (var j = i + 1; j < rects.Length; j++)
            {
                if (PixelRect.IntersectionArea(rects[i], rects[j]) > 0) return false;
            }
        }
        return true;
    }

    static bool Same(PixelRect[] left, PixelRect[] right)
    {
        if (left.Length != right.Length) return false;
        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i]) return false;
        }
        return true;
    }

    static double OverlapRatio(PixelRect a, PixelRect b)
    {
        var area = Math.Min(a.Area, b.Area);
        if (area <= 0) return 0;
        return PixelRect.IntersectionArea(a, b) / (double)area;
    }

    static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    static void Require(bool ok, string message, params PixelRect[] rects)
    {
        if (ok) return;
        throw new InvalidOperationException(message + " " + string.Join("; ", rects));
    }
}

public readonly record struct PixelRect(int X, int Y, int W, int H)
{
    public int Right => X + W;
    public int Bottom => Y + H;
    public long Area => (long)Math.Max(0, W) * Math.Max(0, H);

    public static long IntersectionArea(PixelRect a, PixelRect b)
    {
        var width = (long)Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
        var height = (long)Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
        if (width <= 0 || height <= 0) return 0;
        return width * height;
    }
}
