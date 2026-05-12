using Microsoft.AspNetCore.Razor.TagHelpers;

namespace FlexCms.Framework.TagHelpers;

/// <summary>
/// One-shot copy-to-clipboard button. Uses <c>navigator.clipboard.writeText</c>
/// with a fallback to <c>document.execCommand('copy')</c> for older browsers.
/// On success the button briefly swaps to a check icon + "Copied" tooltip.
///
/// <para>Usage — copy a literal value:</para>
/// <code>&lt;fcms-copy-button value="@Model.ApiKey" label="Copy API key" /&gt;</code>
///
/// <para>Or copy whatever a sibling input currently holds:</para>
/// <code>&lt;fcms-copy-button from="#shareUrl" /&gt;</code>
///
/// <para>
/// Self-contained — emits a button + a tiny inline script per use. No global
/// JS file required. Bootstrap Icons (<c>bi bi-clipboard</c>) used for the icon
/// so it inherits theme colors.
/// </para>
/// </summary>
[HtmlTargetElement("fcms-copy-button")]
public sealed class FcmsCopyButtonTagHelper : TagHelper
{
    /// <summary>Literal value to copy. Mutually exclusive with <see cref="From"/>.</summary>
    [HtmlAttributeName("value")]
    public string? Value { get; set; }

    /// <summary>CSS selector of an input/textarea whose current value to copy. Wins over <see cref="Value"/> if both set.</summary>
    [HtmlAttributeName("from")]
    public string? From { get; set; }

    /// <summary>Visible label. Empty → icon-only button.</summary>
    [HtmlAttributeName("label")]
    public string? Label { get; set; }

    /// <summary>Bootstrap-Icons class. Default <c>bi bi-clipboard</c>.</summary>
    [HtmlAttributeName("icon")]
    public string Icon { get; set; } = "bi bi-clipboard";

    /// <summary>Bootstrap button variant. Default <c>btn-outline-secondary</c>.</summary>
    [HtmlAttributeName("variant")]
    public string Variant { get; set; } = "btn-outline-secondary";

    /// <summary>Button size class — <c>btn-sm</c> / <c>btn-lg</c> / empty.</summary>
    [HtmlAttributeName("size")]
    public string Size { get; set; } = "btn-sm";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var hasFrom = !string.IsNullOrWhiteSpace(From);
        var btnId = "fcms-cpy-" + Guid.NewGuid().ToString("N")[..8];
        var labelHtml = string.IsNullOrEmpty(Label)
            ? ""
            : $" <span class=\"fcms-cpy-label\">{System.Net.WebUtility.HtmlEncode(Label)}</span>";

        // data-fcms-copy attributes carry source info — JS reads them at click time.
        var dataAttr = hasFrom
            ? $"data-fcms-copy-from=\"{System.Net.WebUtility.HtmlEncode(From!)}\""
            : $"data-fcms-copy-value=\"{System.Net.WebUtility.HtmlEncode(Value ?? "")}\"";

        output.TagName = null;
        output.Content.SetHtmlContent($$"""
<button type="button" id="{{btnId}}" class="btn {{Variant}} {{Size}}" {{dataAttr}} title="Copy">
  <i class="{{Icon}}"></i>{{labelHtml}}
</button>
<script>
(function() {
  var btn = document.getElementById('{{btnId}}');
  if (!btn) return;
  btn.addEventListener('click', async function() {
    var v = btn.dataset.fcmsCopyValue;
    if (btn.dataset.fcmsCopyFrom) {
      var src = document.querySelector(btn.dataset.fcmsCopyFrom);
      v = src ? (src.value !== undefined ? src.value : src.textContent) : '';
    }
    try {
      if (navigator.clipboard && window.isSecureContext) {
        await navigator.clipboard.writeText(v);
      } else {
        var ta = document.createElement('textarea');
        ta.value = v; ta.style.position = 'fixed'; ta.style.opacity = '0';
        document.body.appendChild(ta); ta.select();
        document.execCommand('copy');
        document.body.removeChild(ta);
      }
      var icon = btn.querySelector('i');
      var prevIcon = icon.className;
      icon.className = 'bi bi-check-lg text-success';
      btn.setAttribute('title', 'Copied!');
      setTimeout(function() {
        icon.className = prevIcon;
        btn.setAttribute('title', 'Copy');
      }, 1500);
    } catch (e) {
      btn.setAttribute('title', 'Copy failed');
    }
  });
})();
</script>
""");
    }
}
