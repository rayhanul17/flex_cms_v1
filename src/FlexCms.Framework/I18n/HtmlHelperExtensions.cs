using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace FlexCms.Framework.I18n;

/// <summary>
/// Razor helpers for translations. Usage in views:
/// <code>@Html.T("common.save")</code> — returns plain string;
/// <code>@Html.TR("common.save")</code> — returns IHtmlContent (no extra encoding).
/// </summary>
public static class HtmlHelperExtensions
{
    public static string T(this IHtmlHelper helper, string key)
        => helper.ViewContext.HttpContext.RequestServices
            .GetRequiredService<IFcmsTranslator>().T(key);

    public static string T(this IHtmlHelper helper, string key, string lang)
        => helper.ViewContext.HttpContext.RequestServices
            .GetRequiredService<IFcmsTranslator>().T(key, lang);

    public static string T(this IHtmlHelper helper, string key, IReadOnlyDictionary<string, object?> args)
        => helper.ViewContext.HttpContext.RequestServices
            .GetRequiredService<IFcmsTranslator>().T(key, args);

    public static IHtmlContent TR(this IHtmlHelper helper, string key)
        => new HtmlString(helper.T(key));
}
