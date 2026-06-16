using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Seo;

/// <summary>
/// Per-entity SEO metadata kept in a separate table so we don't bloat the
/// core CMS entities (<see cref="Cms.FcmsPage"/>, <see cref="Cms.FcmsPost"/>)
/// with rarely-edited fields. <c>(EntityType, EntityId)</c> is the natural
/// key — at most one row per entity.
///
/// <para>
/// Lookup pattern is the same as <see cref="Cms.CustomFields.FcmsContentMeta"/>:
/// the renderer fetches by EntityType+EntityId at request time. A NULL row
/// = "no overrides", in which case the entity's own <c>MetaTitle</c> /
/// <c>MetaDescription</c> + a few framework defaults render.
/// </para>
/// </summary>
public class FcmsSeoMeta : BaseEfEntity
{
    /// <summary>e.g. <c>"FcmsPage"</c>, <c>"FcmsPost"</c>. Use <see cref="Type.Name"/> on the owner entity.</summary>
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }

    /// <summary>Optional &lt;link rel="canonical"&gt; URL — useful for syndicated posts.</summary>
    public string? CanonicalUrl { get; set; }

    /// <summary>If true, render &lt;meta name="robots" content="noindex,nofollow"&gt;.</summary>
    public bool NoIndex { get; set; }

    public string? OgTitle { get; set; }
    public string? OgDescription { get; set; }
    /// <summary>Public URL of the social-share image. 1200×630 recommended.</summary>
    public string? OgImageUrl { get; set; }
    /// <summary><c>article</c>, <c>website</c>, <c>video</c>, <c>product</c>...</summary>
    public string OgType { get; set; } = "article";

    public string? TwitterCard { get; set; } = "summary_large_image";
    public string? TwitterTitle { get; set; }
    public string? TwitterDescription { get; set; }
    public string? TwitterImageUrl { get; set; }

    /// <summary>Schema.org type — e.g. <c>Article</c>, <c>NewsArticle</c>, <c>BlogPosting</c>, <c>Product</c>, <c>FAQPage</c>.</summary>
    public string SchemaType { get; set; } = "Article";

    /// <summary>
    /// Optional admin-supplied JSON-LD payload. If present, this is rendered
    /// verbatim (still wrapped in &lt;script type="application/ld+json"&gt;).
    /// If blank, <see cref="ISeoService"/> generates a sensible default from
    /// <see cref="SchemaType"/> + the owner entity's title/description/url.
    /// </summary>
    public string? CustomJsonLd { get; set; }
}
