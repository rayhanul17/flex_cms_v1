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

        // position:fixed BOTTOM-right (corner pill) so the banner is
        // unmissable but does NOT overlay or push the rest of the layout.
        // Earlier versions used a top strip which overlapped the topbar
        // and made the hamburger / page title look squashed against the
        // top of the viewport. pointer-events:none lets clicks land on
        // whatever is underneath. Small + corner-pinned = visible to a
        // developer, invisible to layout.
        output.Content.SetHtmlContent(
            $@"<div style=""position:fixed;bottom:8px;right:8px;z-index:1100;background:{color};color:#fff;font-size:.7rem;font-weight:600;padding:3px 10px;letter-spacing:.4px;pointer-events:none;border-radius:999px;box-shadow:0 2px 6px rgba(0,0,0,.25);opacity:.85"">
                {label}
            </div>");
    }
}
