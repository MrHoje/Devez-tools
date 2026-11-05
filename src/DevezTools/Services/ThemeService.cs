using System.Windows;
using System.Windows.Media;

namespace DevezTools.Services;

/// <summary>
/// eghisWorks ThemeService 의 여섯 테마(윗줄 라이트, 아랫줄 다크)를 그대로 쓴다.
/// 앱 리소스의 브러시는 얼려 있으므로 테마마다 새 인스턴스로 바꾸고, XAML 은 DynamicResource, 코드는 SetResourceReference 로 참조한다.
/// </summary>
public static class ThemeService
{
    public const string Minimal = "minimal";
    public const string Soft = "soft";
    public const string Gray = "gray";
    public const string Midnight = "midnight";
    public const string Charcoal = "dark";
    public const string DarkGray = "darkgray";

    public static readonly IReadOnlyList<ThemeOption> Options =
    [
        new(Minimal, "심플", "깔끔하고 선명하게", new ThemePalette(
            Rgb(0xf8fafc), Rgb(0xffffff), Rgb(0xf1f5f9), Rgb(0xe2e8f0), Rgb(0x0f172a), Rgb(0x475569),
            Rgb(0x2563eb), Rgb(0x1d4ed8), Rgb(0xdbeafe), Rgb(0xdc2626), Rgb(0x15803d), Rgb(0xb45309), Colors.White, false)),
        new(Soft, "소프트", "따뜻하고 편안하게", new ThemePalette(
            Rgb(0xf2ede6), Rgb(0xfaf7f2), Rgb(0xece7de), Rgb(0xd8d2c6), Rgb(0x2a2620), Rgb(0x5a5448),
            Rgb(0x5c8c4a), Rgb(0x4e7a3e), Rgb(0xdeecd6), Rgb(0xc24848), Rgb(0x15803d), Rgb(0xa16207), Colors.White, false,
            SegmentSelected: Rgb(0xc8dfb8), SegmentSelectedText: Rgb(0x4e7a3e))),
        new(Gray, "그레이", "차분한 무채색", new ThemePalette(
            Rgb(0xf3f4f6), Rgb(0xffffff), Rgb(0xe5e7eb), Rgb(0xd1d5db), Rgb(0x1f2937), Rgb(0x5f6774),
            Rgb(0x4b5563), Rgb(0x374151), Rgb(0xd6dbe1), Rgb(0xc2413e), Rgb(0x15803d), Rgb(0xb45309), Colors.White, false,
            SegmentSelected: Rgb(0xc7cdd4))),
        new(Midnight, "미드나잇", "밤에도 눈이 편하게", new ThemePalette(
            Rgb(0x111827), Rgb(0x1f2937), Rgb(0x293548), Rgb(0x374151), Rgb(0xf1f5f9), Rgb(0xb4bdca),
            Rgb(0x7cb4fb), Rgb(0x3b82f6), Rgb(0x1e3a5f), Rgb(0xf87171), Rgb(0x34d399), Rgb(0xfacc15), Rgb(0x0f172a), true,
            SegmentSelected: Rgb(0x2d4a6b))),
        new(Charcoal, "차콜", "집중하기 좋게", new ThemePalette(
            Rgb(0x1f1f1e), Rgb(0x272727), Rgb(0x2f2f2f), Rgb(0x404040), Rgb(0xe8e8e8), Rgb(0xaaaaaa),
            Rgb(0xc2622a), Rgb(0xb5571f), Rgb(0x434343), Rgb(0xef4444), Rgb(0x22c55e), Rgb(0xfacc15), Colors.White, true,
            SegmentSelectedText: Rgb(0xf0a070))),
        new(DarkGray, "다크 그레이", "어둡고 차분한 무채색", new ThemePalette(
            Rgb(0x2a2d31), Rgb(0x32353a), Rgb(0x3b3e44), Rgb(0x50545b), Rgb(0xeceef1), Rgb(0xbcc1c8),
            Rgb(0xd1d5db), Rgb(0xe5e7eb), Rgb(0x484c54), Rgb(0xfb8484), Rgb(0x4ade80), Rgb(0xfacc15), Rgb(0x1f2226), true)),
    ];

    public static string Current { get; private set; } = Minimal;

    public static bool IsDark(string key) => (Options.FirstOrDefault(o => o.Key == key) ?? Options[0]).Palette.IsDark;

    /// <summary>저장값이 그 밝기의 테마면 그대로, 아니면 심플·차콜.</summary>
    public static string DefaultFor(bool dark, string saved) =>
        Options.FirstOrDefault(o => o.Key == saved && o.Palette.IsDark == dark)?.Key ?? (dark ? Charcoal : Minimal);

    public static void Apply(string key)
    {
        ThemeOption option = Options.FirstOrDefault(o => o.Key == key) ?? Options[0];
        ThemePalette p = option.Palette;
        ResourceDictionary res = Application.Current.Resources;

        Set(res, "Bg", p.Bg);
        Set(res, "Panel", p.Panel);
        Set(res, "PanelSoft", p.PanelSoft);
        Set(res, "Line", p.Line);
        Set(res, "Text", p.Text);
        Set(res, "TextMuted", p.TextMuted);
        Set(res, "Primary", p.Primary);
        Set(res, "PrimaryHover", p.PrimaryHover);
        Set(res, "PrimarySoft", p.PrimarySoft);
        Set(res, "Danger", p.Danger);
        Set(res, "Success", p.Success);
        Set(res, "Warning", p.Warning);
        Set(res, "OnPrimary", p.OnPrimary);
        Set(res, "SegmentSelected", p.SegmentSelected ?? p.PrimarySoft);
        Set(res, "SegmentSelectedText", p.SegmentSelectedText ?? p.Primary);
        Set(res, "FocusRing", Mix(p.SegmentSelected ?? p.PrimarySoft, p.Primary));

        // 코드 색은 VS Code 기본 라이트·다크 팔레트를 따른다.
        SetBrush(res, "JsonPropertyBrush", p.IsDark ? Rgb(0x9cdcfe) : Rgb(0x0451a5));
        SetBrush(res, "JsonStringBrush", p.IsDark ? Rgb(0xce9178) : Rgb(0xa31515));
        SetBrush(res, "JsonNumberBrush", p.IsDark ? Rgb(0xb5cea8) : Rgb(0x098658));
        SetBrush(res, "JsonBooleanBrush", p.IsDark ? Rgb(0x569cd6) : Rgb(0x0000ff));
        SetBrush(res, "JsonPunctuationBrush", p.TextMuted);
        SetBrush(res, "DiffAddBrush", WithAlpha(p.Success, p.IsDark ? (byte)0x33 : (byte)0x26));
        SetBrush(res, "DiffDelBrush", WithAlpha(p.Danger, p.IsDark ? (byte)0x33 : (byte)0x26));
        SetBrush(res, "SearchHitBrush", WithAlpha(p.Primary, p.IsDark ? (byte)0x55 : (byte)0x40));

        // 어두운 테마는 그림자를 조금 더 진하게
        res["PopupShadowOpacity"] = p.IsDark ? 0.42 : 0.18;
        res["SurfaceShadowOpacity"] = p.IsDark ? 0.25 : 0.05;

        Current = option.Key;
    }

    private static void Set(ResourceDictionary res, string name, Color color)
    {
        res[name + "Color"] = color;
        SetBrush(res, name + "Brush", color);
    }

    private static void SetBrush(ResourceDictionary res, string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        res[key] = brush;
    }

    private static Color Mix(Color a, Color b) =>
        Color.FromRgb((byte)((a.R + b.R) / 2), (byte)((a.G + b.G) / 2), (byte)((a.B + b.B) / 2));

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}

public sealed record ThemeOption(string Key, string Label, string Description, ThemePalette Palette);

public sealed record ThemePalette(
    Color Bg, Color Panel, Color PanelSoft, Color Line, Color Text, Color TextMuted,
    Color Primary, Color PrimaryHover, Color PrimarySoft, Color Danger, Color Success, Color Warning,
    Color OnPrimary, bool IsDark, Color? SegmentSelected = null, Color? SegmentSelectedText = null);
