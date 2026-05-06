using FlexCms.Framework.I18n;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace FlexCms.Tests.Unit.Phase7;

/// <summary>
/// Unit tests for FcmsTranslator: embedded loading, fallback chain, placeholders,
/// and module overrides. No HTTP context — translator runs in standalone mode and
/// falls back to "en" as the default language.
/// </summary>
public class FcmsTranslatorTests
{
    [Fact]
    public void Constructor_loads_embedded_languages()
    {
        var t = new FcmsTranslator();
        Assert.Contains("en", t.SupportedLanguages, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("bn", t.SupportedLanguages, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void T_returns_value_for_known_key_in_current_language()
    {
        var t = new FcmsTranslator();
        // No HTTP context → DefaultLanguage = "en". Direct lang arg overrides.
        Assert.Equal("Save", t.T("common.save", "en"));
        Assert.Equal("সংরক্ষণ", t.T("common.save", "bn"));
    }

    [Fact]
    public void T_falls_back_to_default_when_key_missing_in_requested_lang()
    {
        var t = new FcmsTranslator();
        t.AddOrOverride("bn", new Dictionary<string, string> { ["new.key"] = "" });   // empty, simulate missing
        // Use a key that exists only in en (after we strip it from bn dict above is irrelevant — use a key not present in bn at all)
        var key = "_test_only_in_en_" + Guid.NewGuid();
        t.AddOrOverride("en", new Dictionary<string, string> { [key] = "EnValue" });

        // Request in bn → not found → falls back to en
        Assert.Equal("EnValue", t.T(key, "bn"));
    }

    [Fact]
    public void T_returns_key_when_missing_in_all_languages()
    {
        var t = new FcmsTranslator();
        Assert.Equal("nonexistent.key.xyz", t.T("nonexistent.key.xyz"));
    }

    [Fact]
    public void T_with_args_replaces_placeholders()
    {
        var t = new FcmsTranslator();
        t.AddOrOverride("en", new Dictionary<string, string>
        {
            ["greet"] = "Hello, {name}! You have {count} messages."
        });
        var s = t.T("greet", new Dictionary<string, object?> { ["name"] = "Alice", ["count"] = 5 });
        Assert.Equal("Hello, Alice! You have 5 messages.", s);
    }

    [Fact]
    public void T_with_args_leaves_unmatched_placeholders_untouched()
    {
        var t = new FcmsTranslator();
        t.AddOrOverride("en", new Dictionary<string, string> { ["msg"] = "Hi {name}, your {missing}" });
        var s = t.T("msg", new Dictionary<string, object?> { ["name"] = "Bob" });
        Assert.Equal("Hi Bob, your {missing}", s);
    }

    [Fact]
    public void HasKey_returns_true_only_for_exact_lang_match()
    {
        var t = new FcmsTranslator();
        Assert.True(t.HasKey("common.save", "en"));
        Assert.True(t.HasKey("common.save", "bn"));
        Assert.False(t.HasKey("definitely.not.a.key", "en"));
    }

    [Fact]
    public void AddOrOverride_replaces_existing_key()
    {
        var t = new FcmsTranslator();
        t.AddOrOverride("en", new Dictionary<string, string> { ["common.save"] = "Custom Save" });
        Assert.Equal("Custom Save", t.T("common.save", "en"));
    }

    [Fact]
    public void AddOrOverride_creates_new_language_bucket()
    {
        var t = new FcmsTranslator();
        t.AddOrOverride("fr", new Dictionary<string, string> { ["common.save"] = "Enregistrer" });
        Assert.Contains("fr", t.SupportedLanguages, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Enregistrer", t.T("common.save", "fr"));
    }

    [Fact]
    public void DefaultLanguage_falls_back_to_en_without_http_context()
    {
        var t = new FcmsTranslator();
        Assert.Equal("en", t.DefaultLanguage);
    }

    [Fact]
    public void DefaultLanguage_reads_HttpContext_DefaultLangContextKey_when_set()
    {
        var http = new HttpContextAccessor();
        var ctx = new DefaultHttpContext();
        ctx.Items[SupportedLanguages.DefaultLangContextKey] = "bn";
        http.HttpContext = ctx;

        var t = new FcmsTranslator(http);
        Assert.Equal("bn", t.DefaultLanguage);
    }

    [Fact]
    public void CurrentLanguage_falls_back_to_DefaultLanguage_when_request_key_unset()
    {
        var http = new HttpContextAccessor();
        var ctx = new DefaultHttpContext();
        ctx.Items[SupportedLanguages.DefaultLangContextKey] = "bn";
        // no SupportedLanguages.ContextItemKey set
        http.HttpContext = ctx;

        var t = new FcmsTranslator(http);
        Assert.Equal("bn", t.CurrentLanguage);
    }

    [Fact]
    public void AllKeys_returns_dictionary_for_lang_or_empty_for_missing_lang()
    {
        var t = new FcmsTranslator();
        var en = t.AllKeys("en");
        Assert.NotEmpty(en);
        Assert.Contains("common.save", en.Keys);

        var nope = t.AllKeys("xx");
        Assert.Empty(nope);
    }

    [Fact]
    public void T_skips_meta_object_in_resource_json()
    {
        var t = new FcmsTranslator();
        // The "_meta" property in en.json is a JSON object, not a string — must NOT
        // appear in the loaded dictionary (otherwise lookups would return "{...}").
        Assert.False(t.HasKey("_meta", "en"));
    }
}
