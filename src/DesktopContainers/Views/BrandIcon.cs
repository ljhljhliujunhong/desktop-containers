using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopContainers;

public static class Brand
{
    public const string Name = "莓格";
    public const string EnglishName = "BerryGrid";
}

public static class BrandIcon
{
    public static BitmapSource CreateBitmap(int size)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
            dc.DrawImage(Logo(), new Rect(0, 0, size, size));

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    static BitmapSource Logo()
    {
        if (_logo != null) return _logo;
        var asm = typeof(Brand).Assembly;
        var name = asm.GetManifestResourceNames().First(item => item.EndsWith("logo.png", StringComparison.OrdinalIgnoreCase));
        using var stream = asm.GetManifestResourceStream(name) ?? throw new InvalidOperationException("logo.png");
        var frame = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        frame.Freeze();
        _logo = frame;
        return frame;
    }

    static BitmapSource? _logo;

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

    static byte[] Png(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
