using Microsoft.AspNetCore.Razor.TagHelpers;

namespace FlexCms.Framework.Security;

/// <summary>
/// Renders the honeypot input pair: a wrapper styled completely off-screen +
/// the trap field itself. Both <c>autocomplete="off"</c> and
/// <c>tabindex="-1"</c> so screen-readers + keyboard users skip them.
///
/// Usage in a Razor form:
/// <code>&lt;fcms-honeypot /&gt;</code>
/// </summary>
[HtmlTargetElement("fcms-honeypot", TagStructure = TagStructure.WithoutEndTag)]
public sealed class HoneypotTagHelper : TagHelper
{
    private readonly IFcmsHoneypotService _service;

    public HoneypotTagHelper(IFcmsHoneypotService service) => _service = service;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;
        output.Content.SetHtmlContent(
            $@"<div aria-hidden=""true"" style=""position:absolute;left:-9999px;top:-9999px;height:0;width:0;overflow:hidden"">
                <label>Leave this field empty</label>
                <input type=""text"" name=""{_service.FieldName}"" autocomplete=""off"" tabindex=""-1"" value="""" />
            </div>");
    }
}
