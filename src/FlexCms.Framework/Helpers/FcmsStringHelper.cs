using System.Text;
using System.Text.RegularExpressions;

namespace FlexCms.Framework.Helpers;

/// <summary>
/// String utilities shared across the framework and modules. Methods are pure,
/// null-safe, and allocation-light — call sites can chain freely.
///
/// <para>
/// Slug / pluralization / snake-case / base64 / page-password helpers stay on
/// <see cref="FcmsHelper"/> for backwards compatibility. This class focuses on
/// the everyday "shape this string before showing it" operations a controller
/// or Razor view needs.
/// </para>
/// </summary>
public static class FcmsStringHelper
{
    /// <summary>
    /// Returns at most <paramref name="maxLength"/> characters of
    /// <paramref name="value"/>, appending <paramref name="ellipsis"/> when
    /// truncation happens. Null or empty input returns empty string.
    /// </summary>
    public static string Truncate(string? value, int maxLength, string ellipsis = "…")
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (maxLength <= 0) return "";
        if (value.Length <= maxLength) return value;
        var cut = Math.Max(0, maxLength - ellipsis.Length);
        return value[..cut] + ellipsis;
    }

    private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex HtmlWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Strips every HTML tag and collapses whitespace runs into a single space.
    /// Use for summaries, search indexing, and meta-description generation.
    /// </summary>
    public static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var decoded = System.Net.WebUtility.HtmlDecode(html);
        var stripped = HtmlTagRegex.Replace(decoded, " ");
        return HtmlWhitespaceRegex.Replace(stripped, " ").Trim();
    }

    /// <summary>
    /// Returns the first <paramref name="words"/> whitespace-separated tokens
    /// from <paramref name="value"/>, optionally appending <paramref name="ellipsis"/>
    /// when the source had more words.
    /// </summary>
    public static string FirstWords(string? value, int words, string ellipsis = "…")
    {
        if (string.IsNullOrWhiteSpace(value) || words <= 0) return "";
        var parts = value.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= words) return string.Join(' ', parts);
        return string.Join(' ', parts.Take(words)) + ellipsis;
    }

    /// <summary>
    /// Returns <paramref name="value"/> with the first letter upper-cased and
    /// the rest left untouched. Null/empty input returns empty string.
    /// </summary>
    public static string Capitalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    /// <summary>
    /// URL-encode but preserve common routing punctuation
    /// (<c>/</c>, <c>=</c>, <c>?</c>, <c>&amp;</c>, <c>#</c>, <c>:</c>, <c>-</c>).
    /// Use when you want <c>HttpUtility.UrlEncode</c>'s safety on the
    /// querystring portion of a URL without mangling the path / scheme.
    /// </summary>
    public static string SmartUrlEncode(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        var encoded = Uri.EscapeDataString(url);
        // Restore preserved separators
        return encoded
            .Replace("%2F", "/")
            .Replace("%3D", "=")
            .Replace("%3F", "?")
            .Replace("%26", "&")
            .Replace("%23", "#")
            .Replace("%3A", ":")
            .Replace("%2D", "-");
    }

    private static readonly Regex MultiWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Collapses runs of whitespace (spaces / tabs / newlines) into a single
    /// space and trims the result. Useful before search indexing or fuzzy
    /// comparison.
    /// </summary>
    public static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return MultiWhitespaceRegex.Replace(value, " ").Trim();
    }

    /// <summary>
    /// Removes ASCII control characters (0x00–0x1F, 0x7F) that shouldn't appear
    /// in user-submitted text. Preserves CR/LF/TAB.
    /// </summary>
    public static string SanitizeControl(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c == '\r' || c == '\n' || c == '\t' || c >= 0x20 && c != 0x7F)
                sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Masks the middle of a string while keeping the first
    /// <paramref name="visibleStart"/> and last <paramref name="visibleEnd"/>
    /// characters. Use for displaying credit-card / phone / email previews.
    /// </summary>
    public static string Mask(string? value, int visibleStart = 2, int visibleEnd = 2, char maskChar = '•')
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Length <= visibleStart + visibleEnd) return new string(maskChar, value.Length);
        var maskLen = value.Length - visibleStart - visibleEnd;
        return value[..visibleStart] + new string(maskChar, maskLen) + value[^visibleEnd..];
    }

    /// <summary>
    /// Returns the input split into lines (CR / LF / CRLF aware) with empty
    /// lines trimmed and per-line whitespace normalized.
    /// </summary>
    public static IReadOnlyList<string> SplitLines(string? value)
    {
        if (string.IsNullOrEmpty(value)) return Array.Empty<string>();
        return value
            .Split('\n')
            .Select(l => l.TrimEnd('\r').Trim())
            .Where(l => l.Length > 0)
            .ToArray();
    }
}
