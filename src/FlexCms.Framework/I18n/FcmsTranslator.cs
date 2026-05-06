using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.I18n;

/// <summary>
/// Singleton translator. Loads embedded JSONs once at construction; admin
/// overrides and module additions can be merged later via
/// <see cref="AddOrOverride"/>. Lookups are O(1) dictionary access.
///
/// <para>
/// <b>Scope.</b> The translator never resolves <c>ISettingsService</c> directly
/// (singleton can't take a scoped dependency). Instead,
/// <see cref="LanguageMiddleware"/> writes the resolved current + default
/// language into <see cref="HttpContext.Items"/> on every request and the
/// translator reads them from there. Outside an HTTP context (background
/// services, unit tests) <see cref="DefaultLanguage"/> falls back to
/// <see cref="SupportedLanguages.English"/>.
/// </para>
/// </summary>
public sealed class FcmsTranslator : IFcmsTranslator
{
    private readonly Dictionary<string, Dictionary<string, string>> _byLang = new(StringComparer.OrdinalIgnoreCase);
    private readonly IHttpContextAccessor? _http;
    private readonly ILogger<FcmsTranslator>? _logger;
    private readonly object _lock = new();

    public FcmsTranslator(IHttpContextAccessor? http = null, ILogger<FcmsTranslator>? logger = null)
    {
        _http = http;
        _logger = logger;
        LoadEmbeddedFromAssembly(typeof(FcmsTranslator).Assembly);
    }

    public IReadOnlyList<string> SupportedLanguages
    {
        get { lock (_lock) return _byLang.Keys.ToArray(); }
    }

    public string DefaultLanguage
    {
        get
        {
            var fromCtx = _http?.HttpContext?.Items[I18n.SupportedLanguages.DefaultLangContextKey] as string;
            return string.IsNullOrWhiteSpace(fromCtx) ? I18n.SupportedLanguages.English : fromCtx;
        }
    }

    public string CurrentLanguage
    {
        get
        {
            var fromCtx = _http?.HttpContext?.Items[I18n.SupportedLanguages.ContextItemKey] as string;
            return string.IsNullOrWhiteSpace(fromCtx) ? DefaultLanguage : fromCtx;
        }
    }

    public string T(string key) => T(key, lang: null);

    public string T(string key, string? lang)
    {
        if (string.IsNullOrEmpty(key)) return key ?? "";
        var requested = string.IsNullOrWhiteSpace(lang) ? CurrentLanguage : lang;

        lock (_lock)
        {
            if (_byLang.TryGetValue(requested, out var dict) && dict.TryGetValue(key, out var v))
                return v;

            var def = DefaultLanguage;
            if (!string.Equals(requested, def, StringComparison.OrdinalIgnoreCase)
                && _byLang.TryGetValue(def, out var defDict)
                && defDict.TryGetValue(key, out var dv))
                return dv;
        }

        return key;
    }

    public string T(string key, IReadOnlyDictionary<string, object?> args)
    {
        var s = T(key);
        if (args is null || args.Count == 0) return s;
        foreach (var (name, value) in args)
            s = s.Replace("{" + name + "}", value?.ToString() ?? "", StringComparison.Ordinal);
        return s;
    }

    public bool HasKey(string key, string? lang = null)
    {
        var l = string.IsNullOrWhiteSpace(lang) ? CurrentLanguage : lang;
        lock (_lock)
            return _byLang.TryGetValue(l, out var d) && d.ContainsKey(key);
    }

    public IReadOnlyDictionary<string, string> AllKeys(string? lang = null)
    {
        var l = string.IsNullOrWhiteSpace(lang) ? CurrentLanguage : lang;
        lock (_lock)
            return _byLang.TryGetValue(l, out var d)
                ? new Dictionary<string, string>(d)
                : new Dictionary<string, string>();
    }

    public void AddOrOverride(string lang, IReadOnlyDictionary<string, string> entries)
    {
        if (string.IsNullOrWhiteSpace(lang) || entries is null) return;
        lock (_lock)
        {
            if (!_byLang.TryGetValue(lang, out var dict))
            {
                dict = new Dictionary<string, string>(StringComparer.Ordinal);
                _byLang[lang] = dict;
            }
            foreach (var (k, v) in entries) dict[k] = v;
        }
    }

    /// <summary>
    /// Scan <paramref name="asm"/> for embedded resources whose logical name
    /// contains <c>.i18n.{code}.json</c> and merge each into the in-memory
    /// language map. Modules call this against their own assembly during
    /// startup to ship translations alongside their code.
    /// </summary>
    public void LoadEmbeddedFromAssembly(Assembly asm)
    {
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.IndexOf(".i18n.", StringComparison.OrdinalIgnoreCase) < 0) continue;

            // Resource name is like "FlexCms.Framework.Resources.i18n.en.json".
            // Strip the ".json" suffix, then take the segment after the last dot.
            var withoutExt = name.Substring(0, name.Length - 5);
            var lastDot = withoutExt.LastIndexOf('.');
            if (lastDot < 0) continue;
            var code = withoutExt[(lastDot + 1)..];
            if (string.IsNullOrWhiteSpace(code)) continue;

            try
            {
                using var s = asm.GetManifestResourceStream(name);
                if (s is null) continue;
                var dict = ParseJson(s);
                AddOrOverride(code, dict);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "FcmsTranslator: failed to load embedded i18n resource {Name}", name);
            }
        }
    }

    private static Dictionary<string, string> ParseJson(Stream s)
    {
        using var doc = JsonDocument.Parse(s);
        var root = doc.RootElement;
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.ValueKind != JsonValueKind.Object) return dict;
        foreach (var prop in root.EnumerateObject())
        {
            // Skip "_meta" or any nested object — only flat string entries are translations.
            if (prop.Value.ValueKind == JsonValueKind.String)
                dict[prop.Name] = prop.Value.GetString() ?? "";
        }
        return dict;
    }
}
