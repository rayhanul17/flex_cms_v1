namespace FlexCms.Framework.Themes;

public static class HexColorHelper
{
    /// <summary>
    /// Converts a CSS hex color string to a comma-separated RGB string suitable
    /// for Bootstrap's <c>--bs-*-rgb</c> custom properties (e.g. "13, 110, 253").
    /// Supports 3-char shorthand (#abc → #aabbcc). Returns "0, 0, 0" on any
    /// invalid input.
    /// </summary>
    public static string HexToRgb(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return "0, 0, 0";
        hex = hex.TrimStart('#');
        if (hex.Length == 3) hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
        if (hex.Length != 6) return "0, 0, 0";
        try
        {
            var r = Convert.ToInt32(hex[..2], 16);
            var g = Convert.ToInt32(hex[2..4], 16);
            var b = Convert.ToInt32(hex[4..6], 16);
            return $"{r}, {g}, {b}";
        }
        catch { return "0, 0, 0"; }
    }
}
