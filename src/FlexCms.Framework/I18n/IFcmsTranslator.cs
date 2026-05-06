namespace FlexCms.Framework.I18n;

/// <summary>
/// Loads and serves UI string translations from JSON dictionaries. One singleton
/// holds every supported language. Translations are merged from:
/// <list type="number">
///   <item>Embedded <c>Resources/i18n/{code}.json</c> in the Framework assembly.</item>
///   <item>Optional override <c>App_Data/i18n/{code}.json</c> on disk (admin-editable).</item>
///   <item>Module additions via <see cref="AddOrOverride"/> at startup.</item>
/// </list>
/// Lookup chain for a key:
/// requested language → <see cref="DefaultLanguage"/> → key returned verbatim.
/// </summary>
public interface IFcmsTranslator
{
    /// <summary>Translate <paramref name="key"/> using the current request language.</summary>
    string T(string key);

    /// <summary>Translate <paramref name="key"/> in <paramref name="lang"/>; falls back to default then to key.</summary>
    string T(string key, string? lang);

    /// <summary>
    /// Translate and substitute <c>{name}</c> placeholders from
    /// <paramref name="args"/>. Missing placeholders are left as-is.
    /// </summary>
    string T(string key, IReadOnlyDictionary<string, object?> args);

    /// <summary>True if <paramref name="key"/> exists in <paramref name="lang"/> (no fallback).</summary>
    bool HasKey(string key, string? lang = null);

    /// <summary>All key→value pairs for <paramref name="lang"/> (or current). Read-only snapshot.</summary>
    IReadOnlyDictionary<string, string> AllKeys(string? lang = null);

    /// <summary>Languages that have at least one loaded entry. Always includes <see cref="DefaultLanguage"/>.</summary>
    IReadOnlyList<string> SupportedLanguages { get; }

    /// <summary>The site default language (from SiteSettings.DefaultLanguage). Falls back to "en".</summary>
    string DefaultLanguage { get; }

    /// <summary>The language resolved for this request (LanguageMiddleware sets it via <see cref="SupportedLanguages.ContextItemKey"/>).</summary>
    string CurrentLanguage { get; }

    /// <summary>
    /// Merge or override translations for a single language. Used by modules
    /// during their <c>RegisterServices</c> to ship their own UI strings.
    /// Existing keys are replaced; new keys are added.
    /// </summary>
    void AddOrOverride(string lang, IReadOnlyDictionary<string, string> entries);
}
