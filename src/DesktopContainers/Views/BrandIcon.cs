using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopContainers;

public static class BrandIcon
{
    public static BitmapSource CreateBitmap(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var inset = size * 0.04;
            var geometry = new RectangleGeometry(
                new Rect(inset, inset, size - inset * 2, size - inset * 2),
                size * 0.28,
                size * 0.28);
            var brush = new LinearGradientBrush(
                Color.FromRgb(0xFF, 0x8F, 0xB8),
                Color.FromRgb(0xC6, 0xB4, 0xFF),
                new Point(0, 0),
                new Point(1, 1));
            dc.DrawGeometry(brush, null, geometry);
            var dot = size * 0.16;
            var gap = size * 0.07;
            var origin = size * 0.30;
            var white = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            for (var y = 0; y < 2; y++)
            {
                for (var x = 0; x < 2; x++)
                {
                    dc.DrawRoundedRectangle(
                        white,
                        null,
                        new Rect(origin + x * (dot + gap), origin + y * (dot + gap), dot, dot),
                        dot * 0.28,
                        dot * 0.28);
                }
            }
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static System.Drawing.Icon CreateIcon()
    {
        var source = CreateBitmap(32);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        using var bitmap = new System.Drawing.Bitmap(stream);
        var handle = bitmap.GetHicon();
        try
        {
            using var icon = System.Drawing.Icon.FromHandle(handle);
            return (System.Drawing.Icon)icon.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    public static void SaveCanonical()
    {
        try
        {
            var dir = @"E:\VsCodeProject\AgentCache\generated-images\desktop-containers";
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "app-icon.png");
            if (File.Exists(path)) return;
            var source = CreateBitmap(256);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }
        catch (Exception ex)
        {
            Log.Error("icon", ex);
        }
    }
}
