using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Cms;

/// <summary>
/// Per-language overlay for an <see cref="FcmsPage"/>. The base page row remains
/// authoritative for slug routing and access-control; this row only carries the
/// language-specific copy (title + slug + content + meta). Lookup chain on the
/// frontend: <c>(slug, lang) → translation</c>; if no translation row exists for
/// the requested language, the base page fields are used (no 404).
///
/// Composite uniqueness:
/// <list type="bullet">
///   <item><c>(PageId, LanguageCode)</c> — at most one translation per language.</item>
///   <item><c>(LanguageCode, Slug)</c> — slugs are unique within a language so
///         <c>/bn/about-us</c> and <c>/en/about-us</c> can coexist but two BN
///         pages cannot share a slug.</item>
/// </list>
/// </summary>
public class FcmsPageTranslation : BaseEfEntity
{
    public Guid PageId { get; set; }
    public FcmsPage? Page { get; set; }

    /// <summary>ISO 639-1 code (lowercase). Matches <see cref="I18n.SupportedLanguages"/>.</summary>
    public string LanguageCode { get; set; } = "en";

    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Content { get; set; } = "";
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
}
