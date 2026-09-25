using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DesktopContainers;

public sealed class IconService
{
    readonly Dictionary<string, ImageSource> _cache = new();

    public ImageSource? GetCached(string path, int size)
    {
        var key = Key(path, size);
        return key != null && _cache.TryGetValue(key, out var image) ? image : null;
    }

    public void Load(string path, int size, Action<ImageSource?> ready)
    {
        var cached = GetCached(path, size);
        if (cached != null)
        {
            ready(cached);
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            ready(null);
            return;
        }

        dispatcher.BeginInvoke(() =>
        {
            try
            {
                ready(Extract(path, size));
            }
            catch (Exception ex)
            {
                Log.Error("icon", ex);
                ready(null);
            }
        }, DispatcherPriority.Background);
    }

    public ImageSource? Extract(string path, int size)
    {
        var key = Key(path, size);
        if (key == null) return null;
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var pixels = Math.Clamp(size, 32, 256);
        var located = ShortcutService.IconSource(path);
        var image = ExtractCore(located.file, located.index, pixels)
            ?? ExtractCore(located.file, located.index, 32)
            ?? ExtractCore(path, 0, pixels)
            ?? FromShell(path);
        if (image == null) return null;
        _cache[key] = image;
        return image;
    }

    static ImageSource? ExtractCore(string path, int index, int pixels)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        var hr = NativeMethods.SHDefExtractIcon(path, index, 0, out var large, out var small, (uint)pixels);
        try
        {
            if (hr < 0 && large == IntPtr.Zero && small == IntPtr.Zero)
                return null;
            var handle = large != IntPtr.Zero ? large : small;
            if (handle == IntPtr.Zero) return null;
            var source = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            if (large != IntPtr.Zero) NativeMethods.DestroyIcon(large);
            if (small != IntPtr.Zero && small != large) NativeMethods.DestroyIcon(small);
        }
    }

    static ImageSource? FromShell(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        var info = new NativeMethods.SHFILEINFO();
        var result = NativeMethods.SHGetFileInfo(
            path,
            0,
            ref info,
            (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
            NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            NativeMethods.DestroyIcon(info.hIcon);
        }
    }

    static string? Key(string path, int size)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        return path + "|" + File.GetLastWriteTimeUtc(path).Ticks + "|" + size;
    }
}
