using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace FlexCms.Framework.TagHelpers;

/// <summary>
/// A slug input that auto-fills from a sibling title input until the user
/// types in it manually. Replaces the hand-written "listen to title, normalize,
/// stop on manual edit" JavaScript that was duplicated in every Create form.
///
/// <para>Usage:</para>
/// <code>
/// &lt;input asp-for="Title" id="Title" class="form-control" /&gt;
/// &lt;fcms-slug-input asp-for="Slug" from="#Title" /&gt;
/// </code>
///
/// <para>
/// <b>Server-side normalization</b> mirrors what the emitted JS produces:
/// see <see cref="FlexCms.Framework.Helpers.FcmsHelper.ToSlug"/>. Keep the
/// two in lockstep — if you change one, change the other.
/// </para>
/// </summary>
[HtmlTargetElement("fcms-slug-input", Attributes = "asp-for,from")]
public sealed class FcmsSlugInputTagHelper : TagHelper
{
    private readonly IHtmlGenerator _generator;

    public FcmsSlugInputTagHelper(IHtmlGenerator generator) => _generator = generator;

    [HtmlAttributeName("asp-for")]
    public ModelExpression For { get; set; } = default!;

    /// <summary>CSS selector of the source input — usually <c>"#Title"</c> or <c>"#Name"</c>.</summary>
    [HtmlAttributeName("from")]
    public string From { get; set; } = "";

    /// <summary>Optional input-group prefix like <c>"/"</c> (post slugs) or <c>""</c>.</summary>
    [HtmlAttributeName("prefix")]
    public string? Prefix { get; set; }

    [HtmlAttributeName("placeholder")]
    public string? Placeholder { get; set; }

    [ViewContext, HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = default!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var fieldId = For.Name.Replace('.', '_');
        var fieldName = For.Name;
        var value = For.Model?.ToString() ?? "";

        // Wrap in input-group when a prefix is supplied
        if (!string.IsNullOrEmpty(Prefix))
        {
            output.TagName = "div";
            output.Attributes.SetAttribute("class", "input-group");
            output.TagMode = TagMode.StartTagAndEndTag;
            output.Content.SetHtmlContent(
                $"<span class=\"input-group-text\">{System.Net.WebUtility.HtmlEncode(Prefix)}</span>" +
                BuildInput(fieldId, fieldName, value));
        }
        else
        {
            output.TagName = null;   // render only the <input>
            output.Content.SetHtmlContent(BuildInput(fieldId, fieldName, value));
        }

        // Emit the auto-fill script once per source/target pair. Targets are
        // unique (one slug per form), so duplicate emission isn't a concern.
        output.PostElement.SetHtmlContent($$"""
<script>
(function() {
  var src = document.querySelector({{System.Text.Json.JsonSerializer.Serialize(From)}});
  var dst = document.getElementById({{System.Text.Json.JsonSerializer.Serialize(fieldId)}});
  if (!src || !dst) return;

  // If the user has already typed in the slug, treat it as manual.
  if (dst.value && dst.value.length > 0) dst.dataset.manual = '1';

  function slugify(s) {
    return (s || '')
      .toLowerCase()
      .normalize('NFKD').replace(/[\u0300-\u036f]/g, '')
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '');
  }

  src.addEventListener('input', function() {
    if (!dst.dataset.manual) dst.value = slugify(this.value);
  });
  dst.addEventListener('input', function() { this.dataset.manual = '1'; });
})();
</script>
""");
    }

    private string BuildInput(string id, string name, string value)
    {
        var ph = string.IsNullOrEmpty(Placeholder) ? "" : $" placeholder=\"{System.Net.WebUtility.HtmlEncode(Placeholder)}\"";
        return $"<input type=\"text\" id=\"{id}\" name=\"{name}\" value=\"{System.Net.WebUtility.HtmlEncode(value)}\" class=\"form-control\"{ph} />";
    }
}
