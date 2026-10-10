namespace DesktopContainers;

/// <summary>布局快照为独立副本；屏幕变化时按原显示器的相对位置投影。</summary>
public static class LayoutProjection
{
    /// <summary>每块原屏幕上的排列作为整体移动；放不下时统一缩放，不重排单只容器。</summary>
    public static ScreenLayout Adapt(ScreenLayout source, string key, List<LayoutMonitor> target, PixelRect[] works, bool exact)
    {
        var result = Clone(source);
        result.Key = key;
        result.Monitors = CloneMonitors(target);
        if (works.Length == 0 || target.Count == 0 || result.Spots.Count == 0) return result;
        if (exact)
        {
            foreach (var spot in result.Spots) SetRect(spot, EnsureVisible(SpotRect(spot), works));
            return result;
        }
        var monitors = source.Monitors.Count > 0 ? source.Monitors : InferMonitors(source);
        var groups = Enumerable.Range(0, source.Spots.Count).GroupBy(index =>
            Enumerable.Range(0, monitors.Count).OrderByDescending(monitor => PixelRect.IntersectionArea(
                SpotRect(source.Spots[index]), MonitorRect(monitors[monitor]))).First());
        var groupBounds = new List<(int[] Indices, PixelRect Bounds)>();
        foreach (var group in groups)
        {
            var indices = group.ToArray();
            var from = monitors[group.Key];
            var to = target.FirstOrDefault(monitor => monitor.Device.Length > 0 && monitor.Device == from.Device)
                ?? target.FirstOrDefault(monitor => monitor.Primary == from.Primary)
                ?? target.FirstOrDefault(monitor => monitor.Primary) ?? target[0];
            var work = works.OrderByDescending(area => PixelRect.IntersectionArea(area, MonitorRect(to))).First();
            var bounds = Bounds(indices.Select(index => SpotRect(source.Spots[index])));
            var dpiRatio = Math.Max(48, to.Dpi) / (double)Math.Max(48, from.Dpi);
            var fitScale = Math.Min(1, Math.Min(work.W / (bounds.W * dpiRatio), work.H / (bounds.H * dpiRatio)));
            var factor = dpiRatio * fitScale;
            var width = Math.Max(1, (int)Math.Round(bounds.W * factor));
            var height = Math.Max(1, (int)Math.Round(bounds.H * factor));
            var x = Map(bounds.X, bounds.W, width, from.X, from.Width, work.X, work.W);
            var y = Map(bounds.Y, bounds.H, height, from.Y, from.Height, work.Y, work.H);
            foreach (var index in indices)
            {
                var original = source.Spots[index];
                var spot = result.Spots[index];
                SetRect(spot, new PixelRect(x + (int)Math.Round((original.PixelX - bounds.X) * factor),
                    y + (int)Math.Round((original.PixelY - bounds.Y) * factor),
                    Math.Max(1, (int)Math.Round(SpotRect(original).W * factor)),
                    Math.Max(1, (int)Math.Round(SpotRect(original).H * factor))));
                spot.Width = original.Width * fitScale;
                spot.Height = original.Height * fitScale;
                spot.Scale = original.Scale * fitScale;
            }
            groupBounds.Add((indices, new PixelRect(x, y, width, height)));
        }
        // 拔掉副屏时，只移动或缩放整组，组内的排列始终保留。
        if (groupBounds.Count > 1 && DisplayReflow.HasHeavyOverlap(groupBounds.Select(group => group.Bounds).ToArray(), 0.08))
        {
            var fitted = DisplayReflow.Fit(groupBounds.Select(group => group.Bounds).ToArray(), works);
            for (var i = 0; i < groupBounds.Count; i++)
            {
                var old = groupBounds[i].Bounds;
                var scale = Math.Min(1, Math.Min(fitted[i].W / (double)old.W, fitted[i].H / (double)old.H));
                foreach (var index in groupBounds[i].Indices)
                {
                    var spot = result.Spots[index];
                    SetRect(spot, new PixelRect(fitted[i].X + (int)Math.Round((spot.PixelX - old.X) * scale),
                        fitted[i].Y + (int)Math.Round((spot.PixelY - old.Y) * scale),
                        Math.Max(1, (int)Math.Round(spot.PixelWidth * scale)), Math.Max(1, (int)Math.Round(spot.PixelHeight * scale))));
                    spot.Width *= scale;
                    spot.Height *= scale;
                    spot.Scale *= scale;
                }
            }
        }
        return result;
    }

    static List<LayoutMonitor> InferMonitors(ScreenLayout source)
    {
        var monitors = new List<LayoutMonitor>();
        foreach (var part in source.Key.Split('|'))
        {
            var match = System.Text.RegularExpressions.Regex.Match(part, @"([PS])(-?\d+),(-?\d+),(\d+)x(\d+)(?:@(\d+))?");
            if (!match.Success) continue;
            monitors.Add(new LayoutMonitor
            {
                Primary = match.Groups[1].Value == "P", X = int.Parse(match.Groups[2].Value), Y = int.Parse(match.Groups[3].Value),
                Width = int.Parse(match.Groups[4].Value), Height = int.Parse(match.Groups[5].Value),
                Dpi = match.Groups[6].Success ? uint.Parse(match.Groups[6].Value) : 96
            });
        }
        if (monitors.Count > 0) return monitors;
        var bounds = Bounds(source.Spots.Select(SpotRect));
        return [new LayoutMonitor { Primary = true, X = Math.Min(0, bounds.X), Y = Math.Min(0, bounds.Y),
            Width = Math.Max(1, bounds.Right), Height = Math.Max(1, bounds.Bottom) }];
    }

    static PixelRect MonitorRect(LayoutMonitor monitor) => new(monitor.X, monitor.Y, monitor.Width, monitor.Height);
    static PixelRect SpotRect(ContainerSpot spot) => new(spot.PixelX, spot.PixelY,
        Math.Max(1, spot.PixelWidth > 0 ? spot.PixelWidth : (int)Math.Round(spot.Width)),
        Math.Max(1, spot.PixelHeight > 0 ? spot.PixelHeight : (int)Math.Round(spot.Height)));
    static PixelRect Bounds(IEnumerable<PixelRect> rects)
    {
        var all = rects.ToArray();
        var x = all.Min(rect => rect.X);
        var y = all.Min(rect => rect.Y);
        return new PixelRect(x, y, Math.Max(1, all.Max(rect => rect.Right) - x), Math.Max(1, all.Max(rect => rect.Bottom) - y));
    }
    static void SetRect(ContainerSpot spot, PixelRect rect)
    {
        spot.PixelX = rect.X; spot.PixelY = rect.Y; spot.PixelWidth = rect.W; spot.PixelHeight = rect.H;
    }

    public static ScreenLayout Clone(ScreenLayout layout) => new()
    {
        Key = layout.Key,
        Monitors = CloneMonitors(layout.Monitors),
        Spots = layout.Spots.Select(spot => new ContainerSpot
        {
            Id = spot.Id, PixelX = spot.PixelX, PixelY = spot.PixelY,
            PixelWidth = spot.PixelWidth, PixelHeight = spot.PixelHeight,
            X = spot.X, Y = spot.Y, Width = spot.Width, Height = spot.Height, Scale = spot.Scale
        }).ToList()
    };

    public static List<LayoutMonitor> CloneMonitors(List<LayoutMonitor> monitors) => monitors.Select(monitor => new LayoutMonitor
    {
        Device = monitor.Device, Primary = monitor.Primary, Dpi = monitor.Dpi,
        X = monitor.X, Y = monitor.Y, Width = monitor.Width, Height = monitor.Height
    }).ToList();

    public static PixelRect Project(PixelRect rect, List<LayoutMonitor> source, List<LayoutMonitor> target,
        int? targetWidth = null, int? targetHeight = null)
    {
        if (source.Count == 0 || target.Count == 0) return rect;
        var from = source.OrderByDescending(monitor => PixelRect.IntersectionArea(rect,
            new PixelRect(monitor.X, monitor.Y, monitor.Width, monitor.Height))).First();
        var to = target.FirstOrDefault(monitor => monitor.Device == from.Device)
            ?? target.FirstOrDefault(monitor => monitor.Primary == from.Primary)
            ?? target.FirstOrDefault(monitor => monitor.Primary) ?? target[0];
        var width = targetWidth ?? rect.W;
        var height = targetHeight ?? rect.H;
        var x = Map(rect.X, rect.W, width, from.X, from.Width, to.X, to.Width);
        var y = Map(rect.Y, rect.H, height, from.Y, from.Height, to.Y, to.Height);
        return new PixelRect(x, y, width, height);
    }

    static int Map(int position, int size, int targetRectSize, int sourceStart, int sourceSize, int targetStart, int targetSize)
    {
        var fraction = (position - sourceStart) / (double)Math.Max(1, sourceSize - size);
        return targetStart + (int)Math.Round(Math.Clamp(fraction, 0, 1) * Math.Max(0, targetSize - targetRectSize));
    }

    public static PixelRect EnsureVisible(PixelRect rect, PixelRect[] works)
    {
        if (works.Length == 0) return rect;
        // 阴影略出屏幕和用户故意叠放都不是需要重排的错误。
        var visibleW = Math.Min(80, rect.W);
        var visibleH = Math.Min(80, rect.H);
        if (works.Any(work => Math.Min(rect.X + rect.W, work.X + work.W) - Math.Max(rect.X, work.X) >= visibleW
            && Math.Min(rect.Y + rect.H, work.Y + work.H) - Math.Max(rect.Y, work.Y) >= visibleH)) return rect;
        var nearest = works.OrderBy(work => Distance(rect, work)).First();
        var x = Math.Clamp(rect.X, nearest.X, nearest.X + Math.Max(0, nearest.W - rect.W));
        var y = Math.Clamp(rect.Y, nearest.Y, nearest.Y + Math.Max(0, nearest.H - rect.H));
        return new PixelRect(x, y, rect.W, rect.H);
    }

    static double Distance(PixelRect rect, PixelRect work)
    {
        var dx = (double)rect.X + rect.W / 2.0 - work.X - work.W / 2.0;
        var dy = (double)rect.Y + rect.H / 2.0 - work.Y - work.H / 2.0;
        return dx * dx + dy * dy;
    }
}
