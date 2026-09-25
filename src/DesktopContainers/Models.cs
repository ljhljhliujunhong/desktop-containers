using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DesktopContainers;

public enum TitlePlacement
{
    Top,
    Bottom,
    TopLeft,
    Center,
    Hidden
}

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

public sealed class Appearance : ObservableObject
{
    string _background1 = "#FFF6EC";
    string _background2 = "#FFFFFF";
    string? _background3;
    double _backgroundOpacity = 0.94;
    string _borderColor = "#F0E2D2";
    double _borderOpacity = 0.9;
    double _cornerRadius = 26;
    bool _shadow = true;
    string _titleColor = "#6A5C50";
    double _titleSize = 16;
    double _padding = 16;
    double _iconGap = 10;

    public string Background1 { get => _background1; set => Set(ref _background1, value); }
    public string Background2 { get => _background2; set => Set(ref _background2, value); }
    public string? Background3 { get => _background3; set => Set(ref _background3, value); }
    public double BackgroundOpacity { get => _backgroundOpacity; set => Set(ref _backgroundOpacity, Math.Clamp(value, 0, 1)); }
    public string BorderColor { get => _borderColor; set => Set(ref _borderColor, value); }
    public double BorderOpacity { get => _borderOpacity; set => Set(ref _borderOpacity, Math.Clamp(value, 0, 1)); }
    public double CornerRadius { get => _cornerRadius; set => Set(ref _cornerRadius, Math.Clamp(value, 8, 40)); }
    public bool Shadow { get => _shadow; set => Set(ref _shadow, value); }
    public string TitleColor { get => _titleColor; set => Set(ref _titleColor, value); }
    public double TitleSize { get => _titleSize; set => Set(ref _titleSize, Math.Clamp(value, 12, 28)); }
    public double Padding { get => _padding; set => Set(ref _padding, Math.Clamp(value, 8, 32)); }
    public double IconGap { get => _iconGap; set => Set(ref _iconGap, Math.Clamp(value, 2, 28)); }

    public Appearance Clone()
    {
        var copy = new Appearance();
        copy.CopyFrom(this);
        return copy;
    }

    public void CopyFrom(Appearance other)
    {
        Background1 = other.Background1;
        Background2 = other.Background2;
        Background3 = other.Background3;
        BackgroundOpacity = other.BackgroundOpacity;
        BorderColor = other.BorderColor;
        BorderOpacity = other.BorderOpacity;
        CornerRadius = other.CornerRadius;
        Shadow = other.Shadow;
        TitleColor = other.TitleColor;
        TitleSize = other.TitleSize;
        Padding = other.Padding;
        IconGap = other.IconGap;
    }
}

public sealed class ThemeEdit
{
    public string Id { get; set; } = "";
    public Appearance Appearance { get; set; } = new();
}

public sealed class ThemeDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool BuiltIn { get; set; }
    public Appearance Appearance { get; set; } = new();
}

public sealed class AppEntry
{
    public string Id { get; set; } = "";
    public string FileName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public string Key { get; set; } = "";
    public bool Missing { get; set; }
}

public sealed class ContainerModel : ObservableObject
{
    string _name = "新容器";
    bool _locked;
    bool _showTitle = true;
    TitlePlacement _titlePlacement = TitlePlacement.Top;
    double _iconSize = 56;
    bool _showNames = true;

    public string Id { get; set; } = "";
    public string Name { get => _name; set => Set(ref _name, value); }
    public double X { get; set; } = 96;
    public double Y { get; set; } = 72;
    public double Width { get; set; } = 440;
    public double Height { get; set; } = 320;
    public int PixelX { get; set; }
    public int PixelY { get; set; }
    public bool HasPixelPosition { get; set; }
    public string? MonitorDevice { get; set; }
    public bool Locked { get => _locked; set => Set(ref _locked, value); }
    public bool ShowTitle { get => _showTitle; set => Set(ref _showTitle, value); }
    public TitlePlacement TitlePlacement { get => _titlePlacement; set => Set(ref _titlePlacement, value); }
    public double IconSize { get => _iconSize; set => Set(ref _iconSize, Math.Clamp(value, 32, 96)); }
    public bool ShowNames { get => _showNames; set => Set(ref _showNames, value); }
    public string ThemeId { get; set; } = ThemeCatalog.DefaultId;
    public Appearance Appearance { get; set; } = new();
    public ObservableCollection<AppEntry> Apps { get; set; } = new();
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class AppSettings
{
    public bool LaunchAtStartup { get; set; }
    public bool ShowTitlesByDefault { get; set; } = true;
    public double DefaultIconSize { get; set; } = 56;
    public string DefaultThemeId { get; set; } = ThemeCatalog.DefaultId;
    public List<ThemeEdit> ThemeEdits { get; set; } = new();
    public bool AnimationsEnabled { get; set; } = true;
    public bool EditMode { get; set; }
    public bool CompletedOnboarding { get; set; }
}

public sealed class LayoutDocument
{
    public int Version { get; set; } = 1;
    public AppSettings Settings { get; set; } = new();
    public ObservableCollection<ThemeDefinition> CustomThemes { get; set; } = new();
    public ObservableCollection<ContainerModel> Containers { get; set; } = new();
}

public static class Paint
{
    static readonly Dictionary<int, SolidColorBrush> Cache = new();

    public static Color Ink { get; } = Color.FromRgb(0x2C, 0x2A, 0x33);
    public static Color Muted { get; } = Color.FromRgb(0x8B, 0x84, 0x94);
    public static Color Accent { get; } = Color.FromRgb(0xE8, 0x5A, 0x8C);
    public static Color Line { get; } = Color.FromRgb(0xF1, 0xE4, 0xEC);
    public static Color Paper { get; } = Color.FromRgb(0xFF, 0xF7, 0xF4);

    public static Color Hex(string? hex) => TryHex(hex) ?? Color.FromRgb(0xFF, 0xB7, 0xD1);

    public static Color? TryHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var s = hex.Trim();
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length == 3)
            s = string.Concat(s.Select(ch => $"{ch}{ch}"));
        if (s.Length is not 6 and not 8) return null;
        if (!int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n))
            return null;
        if (s.Length == 6)
            return Color.FromRgb((byte)(n >> 16), (byte)(n >> 8), (byte)n);
        return Color.FromArgb((byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n);
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static Color WithAlpha(Color color, double opacity)
    {
        var alpha = (byte)Math.Clamp((int)Math.Round(opacity * 255), 0, 255);
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    public static Color MixWhite(Color color, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)(color.R + (255 - color.R) * amount),
            (byte)(color.G + (255 - color.G) * amount),
            (byte)(color.B + (255 - color.B) * amount));
    }

    public static Color Darken(Color color, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        var keep = 1 - amount;
        return Color.FromRgb((byte)(color.R * keep), (byte)(color.G * keep), (byte)(color.B * keep));
    }

    public static void ApplyTitlePlate(Border plate, Color ink)
    {
        var lightInk = ink.R * 0.299 + ink.G * 0.587 + ink.B * 0.114 > 186;
        if (lightInk)
        {
            plate.Background = Brush(Color.FromArgb(158, 18, 22, 30));
            plate.BorderBrush = Brush(WithAlpha(Colors.White, 0.42));
        }
        else
        {
            var fill = MixWhite(ink, 0.9);
            plate.Background = Brush(Color.FromArgb(238, fill.R, fill.G, fill.B));
            plate.BorderBrush = Brush(WithAlpha(ink, 0.38));
        }
        plate.BorderThickness = new Thickness(1);
        plate.CornerRadius = new CornerRadius(9);
        plate.Padding = new Thickness(10, 2, 10, 2);
    }

    public static SolidColorBrush Brush(Color color)
    {
        var key = (color.A << 24) | (color.R << 16) | (color.G << 8) | color.B;
        if (Cache.TryGetValue(key, out var cached))
            return cached;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Cache[key] = brush;
        return brush;
    }
}

public static class AppearancePainter
{
    public static LinearGradientBrush Background(Appearance appearance)
    {
        var opacity = Math.Clamp(appearance.BackgroundOpacity, 0, 1);
        var brush = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(1, 1)
        };
        brush.GradientStops.Add(new GradientStop(Paint.WithAlpha(Paint.Hex(appearance.Background1), opacity), 0));
        if (!string.IsNullOrWhiteSpace(appearance.Background3))
        {
            brush.GradientStops.Add(new GradientStop(Paint.WithAlpha(Paint.Hex(appearance.Background2), opacity), 0.52));
            brush.GradientStops.Add(new GradientStop(Paint.WithAlpha(Paint.Hex(appearance.Background3), opacity), 1));
        }
        else
        {
            brush.GradientStops.Add(new GradientStop(Paint.WithAlpha(Paint.Hex(appearance.Background2), opacity), 1));
        }
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }

    public static SolidColorBrush Border(Appearance appearance) =>
        Paint.Brush(Paint.WithAlpha(Paint.Hex(appearance.BorderColor), appearance.BorderOpacity));
}

public static class ThemeCatalog
{
    public const string DefaultId = "strawberry-milk";

    static readonly ThemeDefinition[] BuiltInList =
    [
        Theme("strawberry-milk", "草莓牛奶", "#FFD0E0", "#FFF4F8", null, 0.94, "#FFB3CE", 0.9, 26, true, "#8A3D5C"),
        Theme("sky-blue", "天空蓝", "#D4EEFF", "#F5FBFF", null, 0.94, "#B9DFFF", 0.95, 26, true, "#2F6484"),
        Theme("mint", "薄荷绿", "#D5F6E8", "#F4FFFB", null, 0.94, "#B7E6D0", 0.95, 26, true, "#2A7358"),
        Theme("lemon", "柠檬黄", "#FFF1BE", "#FFFDF4", null, 0.95, "#F0D98A", 0.9, 26, true, "#7A6424"),
        Theme("peach", "蜜桃粉", "#FFE1D4", "#FFF7F3", null, 0.94, "#FFCDBB", 0.9, 26, true, "#8A4E3E"),
        Theme("lavender", "薰衣草紫", "#E4DBFF", "#F8F6FF", null, 0.94, "#D4C8FB", 0.95, 26, true, "#5C4D86"),
        Theme("cream", "奶油白", "#FFF6EC", "#FFFFFF", null, 0.96, "#F0E2D2", 0.9, 24, true, "#6A5C50"),
        Theme("glass", "玻璃透明", "#FFFFFF", "#F3F8FF", null, 0.46, "#FFFFFF", 0.8, 28, true, "#2C3A48"),
        Theme("aurora", "Aurora", "#C9D4FF", "#F8C6E4", "#B7F3DE", 0.9, "#FFFFFF", 0.7, 28, true, "#4A3E68"),
        Theme("candy", "Candy", "#FFB7D5", "#C8F6EA", null, 0.92, "#FFFFFF", 0.82, 30, true, "#7A3E5C")
    ];

    public static IReadOnlyList<ThemeDefinition> BuiltIns => BuiltInList;

    public static IReadOnlyList<ThemeDefinition> All(IEnumerable<ThemeDefinition>? custom)
    {
        var list = new List<ThemeDefinition>(BuiltInList);
        if (custom != null) list.AddRange(custom);
        return list;
    }

    public static ThemeDefinition? Find(string? id, IEnumerable<ThemeDefinition>? custom = null)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var built = BuiltInList.FirstOrDefault(theme => theme.Id == id);
        if (built != null) return built;
        return custom?.FirstOrDefault(theme => theme.Id == id);
    }

    public static Appearance CreateAppearance(string? id, IEnumerable<ThemeDefinition>? custom = null)
    {
        var theme = Find(id, custom) ?? BuiltInList[0];
        return theme.Appearance.Clone();
    }

    static ThemeDefinition Theme(
        string id,
        string name,
        string background1,
        string background2,
        string? background3,
        double opacity,
        string border,
        double borderOpacity,
        double radius,
        bool shadow,
        string title)
    {
        return new ThemeDefinition
        {
            Id = id,
            Name = name,
            BuiltIn = true,
            Appearance = new Appearance
            {
                Background1 = background1,
                Background2 = background2,
                Background3 = background3,
                BackgroundOpacity = opacity,
                BorderColor = border,
                BorderOpacity = borderOpacity,
                CornerRadius = radius,
                Shadow = shadow,
                TitleColor = title,
                TitleSize = 16,
                Padding = 16,
                IconGap = 10
            }
        };
    }
}

public static class JsonOpts
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
}
