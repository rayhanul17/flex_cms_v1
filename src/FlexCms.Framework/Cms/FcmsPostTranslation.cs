using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Cms;

/// <summary>
/// Per-language overlay for an <see cref="FcmsPost"/>. See
/// <see cref="FcmsPageTranslation"/> for the routing + fallback contract — the
/// post variant additionally carries an excerpt and translates the same set of
/// SEO meta fields.
/// </summary>
public class FcmsPostTranslation : BaseEfEntity
{
    public Guid PostId { get; set; }
    public FcmsPost? Post { get; set; }

    public string LanguageCode { get; set; } = "en";

    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Excerpt { get; set; }
    public string Content { get; set; } = "";
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
}
