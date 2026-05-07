namespace FlexCms.Framework.Seo;

/// <summary>
/// Read/write per-entity SEO metadata + render helpers.
///
/// <para>
/// Keep the rendering logic on the service so it's reusable from controllers,
/// view components, and module-defined renderers. The renderer never throws —
/// missing data means "fall back to the owner entity / site defaults".
/// </para>
/// </summary>
public interface ISeoService
{
    /// <summary>Lookup a single entity's SEO row. Returns null when none persisted yet.</summary>
    Task<FcmsSeoMeta?> GetAsync(string entityType, Guid entityId, CancellationToken ct = default);

    /// <summary>Upsert (create-or-update) the SEO row. <c>EntityType</c> + <c>EntityId</c> are the natural key.</summary>
    Task SaveAsync(FcmsSeoMeta meta, CancellationToken ct = default);

    /// <summary>
    /// Resolve the effective head tags for an entity — merges the per-entity
    /// row with sensible fallbacks (entity's own MetaTitle/Description, site
    /// name from <c>SiteSettings</c>). Returns the rendered HTML markup ready
    /// to inject into the layout's &lt;head&gt;.
    /// </summary>
    Task<string> RenderHeadTagsAsync(SeoRenderContext ctx, CancellationToken ct = default);

    /// <summary>
    /// Render JSON-LD <c>&lt;script type="application/ld+json"&gt;</c> for an
    /// entity. Honours <see cref="FcmsSeoMeta.CustomJsonLd"/> if set; otherwise
    /// generates a default from the owner's data + <see cref="FcmsSeoMeta.SchemaType"/>.
    /// </summary>
    Task<string> RenderJsonLdAsync(SeoRenderContext ctx, CancellationToken ct = default);
}

/// <summary>
/// Inputs the renderer needs to fill in fallbacks. The owner entity supplies
/// these — keeps the service decoupled from EF + concrete entity types so
/// modules can use it for their own content too.
/// </summary>
public sealed record SeoRenderContext(
    string EntityType,
    Guid EntityId,
    string Title,
    string? Description,
    string CanonicalUrl,
    string? FeaturedImageUrl = null,
    string? AuthorName = null,
    DateTime? PublishedAt = null,
    DateTime? UpdatedAt = null);
