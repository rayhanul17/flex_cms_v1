namespace FlexCms.Framework.Accessibility;

/// <summary>
/// WCAG 2.1 contrast ratio + AA/AAA pass/fail (Phase 16 — Issue 108).
/// Used by the theme editor to warn admins when their picked colors
/// fall below the legibility floor.
///
/// <para>
/// Algorithm matches the W3C spec:
/// </para>
/// <list type="number">
///   <item>For each color, compute relative luminance: gamma-adjust each sRGB channel, then weighted sum (R*0.2126 + G*0.7152 + B*0.0722).</item>
///   <item>Contrast = (lighter + 0.05) / (darker + 0.05). Range: 1.0 (identical) → 21.0 (white-on-black).</item>
/// </list>
///
/// <para>
/// Thresholds:
/// <list type="bullet">
///   <item>AA normal text: ≥ 4.5:1</item>
///   <item>AA large text (18pt+ or 14pt+ bold): ≥ 3:1</item>
///   <item>AAA normal: ≥ 7:1; AAA large: ≥ 4.5:1</item>
/// </list>
/// </para>
/// </summary>
public static class WcagContrast
{
    public const double AaNormalThreshold = 4.5;
    public const double AaLargeThreshold = 3.0;
    public const double AaaNormalThreshold = 7.0;
    public const double AaaLargeThreshold = 4.5;

    /// <summary>
    /// Compute the contrast ratio between two colors. Hex values like
    /// <c>"#ffffff"</c>, <c>"#fff"</c>, or <c>"ffffff"</c>. Returns 0 on
    /// malformed input — the admin UI treats 0 as "unable to evaluate".
    /// </summary>
    public static double Ratio(string hexA, string hexB)
    {
        if (!TryParse(hexA, out var rgbA) || !TryParse(hexB, out var rgbB)) return 0;
        var lA = Luminance(rgbA);
        var lB = Luminance(rgbB);
        var lighter = Math.Max(lA, lB);
        var darker = Math.Min(lA, lB);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>True when the ratio meets WCAG AA for normal-size body text.</summary>
    public static bool MeetsAa(string fg, string bg) => Ratio(fg, bg) >= AaNormalThreshold;

    /// <summary>Levels achieved at the given ratio — for badges in the admin theme editor.</summary>
    public static ContrastLevels Evaluate(double ratio) => new(
        AaNormal: ratio >= AaNormalThreshold,
        AaLarge: ratio >= AaLargeThreshold,
        AaaNormal: ratio >= AaaNormalThreshold,
        AaaLarge: ratio >= AaaLargeThreshold);

    /// <summary>WCAG sRGB → relative luminance.</summary>
    private static double Luminance((int R, int G, int B) rgb)
    {
        var r = Channel(rgb.R / 255.0);
        var g = Channel(rgb.G / 255.0);
        var b = Channel(rgb.B / 255.0);
        return r * 0.2126 + g * 0.7152 + b * 0.0722;
    }

    private static double Channel(double v) =>
        v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);

    private static bool TryParse(string hex, out (int R, int G, int B) rgb)
    {
        rgb = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var s = hex.Trim().TrimStart('#');
        // Expand 3-char shorthand (#fff → #ffffff).
        if (s.Length == 3) s = $"{s[0]}{s[0]}{s[1]}{s[1]}{s[2]}{s[2]}";
        if (s.Length != 6) return false;
        if (!int.TryParse(s.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r)) return false;
        if (!int.TryParse(s.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g)) return false;
        if (!int.TryParse(s.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b)) return false;
        rgb = (r, g, b);
        return true;
    }
}

public sealed record ContrastLevels(bool AaNormal, bool AaLarge, bool AaaNormal, bool AaaLarge);
