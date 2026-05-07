using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Hosting;

namespace FlexCms.Framework.Hosting;

/// <summary>
/// Renders an unmissable banner across the top of the page when the host is
/// running in any environment OTHER than Production. Helps prevent the
/// classic "I just edited prod thinking it was staging" mistake.
///
/// Usage in a layout:
/// <code>&lt;fcms-env-banner /&gt;</code>
/// </summary>
[HtmlTargetElement("fcms-env-banner", TagStructure = TagStructure.WithoutEndTag)]
public sealed class EnvironmentBannerTagHelper : TagHelper
{
    private readonly IWebHostEnvironment _env;

    public EnvironmentBannerTagHelper(IWebHostEnvironment env) => _env = env;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;

        if (_env.IsProduction())
        {
            output.SuppressOutput();
            return;
        }

        var (color, label) = _env.EnvironmentName switch
        {
            "Development" => ("#dc3545", "DEVELOPMENT ENVIRONMENT"),
            "Staging" => ("#fd7e14", "STAGING ENVIRONMENT"),
            _ => ("#6f42c1", $"{_env.EnvironmentName.ToUpperInvariant()} ENVIRONMENT")
        };

        // position:fixed at the top so the banner overlays page chrome
        // without participating in the parent's layout. Without this, when
        // the host's body is a flex container the banner becomes a flex
        // child and reserves a vertical strip of whitespace between
        // siblings (the sidebar and the main-content margin).
        // pointer-events:none lets clicks land on whatever is underneath.
        output.Content.SetHtmlContent(
            $@"<div style=""position:fixed;top:0;left:0;right:0;z-index:1100;background:{color};color:#fff;text-align:center;font-size:.75rem;font-weight:600;padding:2px 0;letter-spacing:.5px;pointer-events:none"">
                {label}
            </div>");
    }
}
