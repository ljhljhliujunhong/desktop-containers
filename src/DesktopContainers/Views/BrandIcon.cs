using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopContainers;

public static class Brand
{
    public const string Name = "澄格";
}

public static class BrandIcon
{
    public static BitmapSource CreateBitmap(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            Draw(dc, size);

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

    public static void SaveIco(string path)
    {
        var sizes = new[] { 16, 24, 32, 48, 64, 256 };
        var pngs = sizes.Select(size => Png(CreateBitmap(size))).ToArray();
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)pngs.Length);
        var offset = 6 + 16 * pngs.Length;
        for (var i = 0; i < pngs.Length; i++)
        {
            var dim = sizes[i] >= 256 ? 0 : sizes[i];
            writer.Write((byte)dim);
            writer.Write((byte)dim);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(pngs[i].Length);
            writer.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var png in pngs)
            writer.Write(png);
    }

    public static void SaveCanonical()
    {
        try
        {
            var dir = @"E:\VsCodeProject\AgentCache\generated-images\desktop-containers";
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "app-icon.png");
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

    static void Draw(DrawingContext dc, int size)
    {
        var inset = size * 0.05;
        var shell = new Rect(inset, inset, size - inset * 2, size - inset * 2);
        var shellBrush = new LinearGradientBrush(
            Color.FromRgb(0xFF, 0xB4, 0xD2),
            Color.FromRgb(0xB7, 0xA4, 0xF4),
            new Point(0, 0),
            new Point(1, 1));
        dc.DrawRoundedRectangle(shellBrush, null, shell, size * 0.28, size * 0.28);

        var pad = size * 0.2;
        var card = new Rect(pad, pad, size - pad * 2, size - pad * 2);
        dc.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromArgb(236, 255, 255, 255)),
            null,
            card,
            size * 0.16,
            size * 0.16);

        var pillH = Math.Max(2, size * 0.075);
        var pillW = card.Width * 0.46;
        var pill = new Rect(card.X + (card.Width - pillW) / 2, card.Y + size * 0.09, pillW, pillH);
        dc.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromRgb(0xE8, 0x5A, 0x8C)),
            null,
            pill,
            pillH / 2,
            pillH / 2);

        var dot = size * 0.1;
        var gap = size * 0.045;
        var total = dot * 3 + gap * 2;
        var start = card.X + (card.Width - total) / 2;
        var y = card.Bottom - size * 0.13 - dot;
        var colors = new[]
        {
            Color.FromRgb(0x6E, 0x62, 0xE0),
            Color.FromRgb(0xE8, 0x5A, 0x8C),
            Color.FromRgb(0x3F, 0xC4, 0xB0)
        };
        for (var i = 0; i < 3; i++)
        {
            var center = new Point(start + dot / 2 + i * (dot + gap), y + dot / 2);
            dc.DrawEllipse(new SolidColorBrush(colors[i]), null, center, dot / 2, dot / 2);
        }
    }

    static byte[] Png(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
