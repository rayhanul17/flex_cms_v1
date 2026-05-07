using System.Text;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace FlexCms.Framework.ImageOptimization;

/// <summary>
/// Razor tag helper that emits a <c>&lt;picture&gt;</c> element with WebP
/// + responsive srcset, falling back to the original on browsers that
/// don't support WebP. Pairs with <see cref="IImageOptimizer"/>'s
/// filename convention.
///
/// <para>
/// Usage in Razor:
/// </para>
/// <code>
/// &lt;fcms-picture src="/uploads/hero.jpg" alt="Hero" widths="640,1024,1920" /&gt;
/// </code>
///
/// <para>
/// Output: a <c>&lt;picture&gt;</c> with a WebP <c>&lt;source srcset&gt;</c>
/// + a fallback <c>&lt;img&gt;</c> with <c>loading="lazy"</c>. If the
/// <c>widths</c> attr is omitted, a single full-size WebP is emitted.
/// </para>
/// </summary>
[HtmlTargetElement("fcms-picture", Attributes = "src", TagStructure = TagStructure.WithoutEndTag)]
public sealed class PictureTagHelper : TagHelper
{
    public string Src { get; set; } = "";
    public string Alt { get; set; } = "";
    public string? Widths { get; set; }
    public string? CssClass { get; set; }
    public string Sizes { get; set; } = "(max-width: 640px) 100vw, (max-width: 1024px) 75vw, 50vw";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (string.IsNullOrWhiteSpace(Src))
        {
            output.SuppressOutput();
            return;
        }

        // Convert /uploads/hero.jpg → /uploads/hero (without extension).
        var dir = (Path.GetDirectoryName(Src) ?? "").Replace('\\', '/');
        var baseName = Path.GetFileNameWithoutExtension(Src);
        var basePath = string.IsNullOrEmpty(dir) ? baseName : $"{dir}/{baseName}";

        output.TagName = "picture";
        output.TagMode = TagMode.StartTagAndEndTag;

        var sb = new StringBuilder();

        // Build the srcset only when widths are supplied — single fallback
        // WebP otherwise.
        if (!string.IsNullOrWhiteSpace(Widths))
        {
            var widths = Widths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(w => int.TryParse(w, out var n) ? n : 0)
                .Where(n => n > 0)
                .OrderBy(n => n)
                .ToArray();

            if (widths.Length > 0)
            {
                var srcset = string.Join(", ", widths.Select(w => $"{basePath}-{w}w.webp {w}w"));
                sb.Append($"<source type=\"image/webp\" srcset=\"{srcset}\" sizes=\"{Sizes}\" />");
            }
            else
            {
                sb.Append($"<source type=\"image/webp\" srcset=\"{basePath}.webp\" />");
            }
        }
        else
        {
            sb.Append($"<source type=\"image/webp\" srcset=\"{basePath}.webp\" />");
        }

        var classAttr = string.IsNullOrEmpty(CssClass) ? "" : $" class=\"{CssClass}\"";
        sb.Append($"<img src=\"{Src}\" alt=\"{Alt}\" loading=\"lazy\" decoding=\"async\"{classAttr} />");

        output.Content.SetHtmlContent(sb.ToString());
    }
}
