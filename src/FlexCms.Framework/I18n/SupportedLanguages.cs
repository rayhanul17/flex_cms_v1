namespace FlexCms.Framework.I18n;

/// <summary>
/// Built-in language list (Phase 7). Can be extended by modules dropping their
/// own <c>i18n/{code}.json</c> files into <c>App_Data/i18n/</c> at runtime.
/// </summary>
public static class SupportedLanguages
{
    public const string English = "en";
    public const string Bengali = "bn";

    /// <summary>Cookie name used by <see cref="LanguageMiddleware"/>.</summary>
    public const string CookieName = "fcms_ui_lang";

    /// <summary>HttpContext.Items key carrying the resolved language code.</summary>
    public const string ContextItemKey = "FcmsLanguage";

    /// <summary>HttpContext.Items key carrying the site's default language (set by middleware).</summary>
    public const string DefaultLangContextKey = "FcmsDefaultLanguage";
}
