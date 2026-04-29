using System.Text.RegularExpressions;

namespace FlexCms.Framework.Cms;

/// <summary>
/// Minimal allow-list HTML sanitizer for CMS content.
/// Strips script/style/iframe/form tags and on* event attributes.
/// Safe subset retained: block elements, inline formatting, links (href only), images (src/alt).
/// </summary>
public static partial class HtmlSanitizer
{
    // Tags that are unconditionally removed along with their content
    private static readonly string[] DangerousTags = ["script", "style", "iframe", "frame", "object", "embed", "form", "base", "meta", "link"];

    public static string Sanitize(string html)
    {
        if (string.IsNullOrEmpty(html)) return html;

        // Remove dangerous tags + their content
        foreach (var tag in DangerousTags)
            html = RemoveTagWithContent().Replace(html, m =>
                string.Equals(m.Groups["tag"].Value, tag, StringComparison.OrdinalIgnoreCase) ? "" : m.Value);

        // Strip on* event attributes (onclick, onload, onerror, …)
        html = StripEventAttributes().Replace(html, "");

        // Strip javascript: hrefs/srcs
        html = StripJsProtocol().Replace(html, "$1=\"#\"");

        return html;
    }

    [GeneratedRegex(@"<(?<tag>script|style|iframe|frame|object|embed|form|base|meta|link)[\s\S]*?</\k<tag>\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex RemoveTagWithContent();

    [GeneratedRegex(@"\s+on\w+\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+)", RegexOptions.IgnoreCase)]
    private static partial Regex StripEventAttributes();

    [GeneratedRegex(@"(href|src)\s*=\s*""javascript:[^""]*""", RegexOptions.IgnoreCase)]
    private static partial Regex StripJsProtocol();
}
