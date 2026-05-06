namespace FlexCms.Framework.Cms;

public interface IPostService
{
    Task<FcmsPost?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<FcmsPost?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<List<FcmsPost>> GetAllAsync(CancellationToken ct = default);
    Task<List<FcmsPost>> GetPublishedAsync(CancellationToken ct = default);
    Task<List<FcmsPost>> GetByCategoryAsync(Guid categoryId, CancellationToken ct = default);
    Task<bool> SlugExistsAsync(string slug, Guid? excludeId = null, CancellationToken ct = default);
    Task<FcmsPost> CreateAsync(FcmsPost post, IEnumerable<string> tagSlugs, CancellationToken ct = default);
    Task UpdateAsync(FcmsPost post, IEnumerable<string> tagSlugs, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task IncrementViewCountAsync(Guid id, CancellationToken ct = default);
    Task<List<FcmsPost>> GetDeletedAsync(CancellationToken ct = default);
    Task RestoreAsync(Guid id, CancellationToken ct = default);
    Task HardDeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>Returns tag slugs for a post without relying on navigation-property loading.</summary>
    Task<List<string>> GetTagSlugsAsync(Guid postId, CancellationToken ct = default);

    // ── Translations (Phase 7) ───────────────────────────────────────────────

    /// <summary>
    /// Resolve a post by language-aware slug. Translation slug match wins, then
    /// base slug. Returns the base post (for routing/access) and the matching
    /// translation if any.
    /// </summary>
    Task<(FcmsPost Post, FcmsPostTranslation? Translation)?> ResolveBySlugAsync(string slug, string lang, CancellationToken ct = default);

    Task<List<FcmsPostTranslation>> GetTranslationsAsync(Guid postId, CancellationToken ct = default);
    Task<FcmsPostTranslation?> GetTranslationAsync(Guid postId, string lang, CancellationToken ct = default);
    Task<FcmsPostTranslation> SaveTranslationAsync(FcmsPostTranslation tr, CancellationToken ct = default);
    Task DeleteTranslationAsync(Guid translationId, CancellationToken ct = default);
}
