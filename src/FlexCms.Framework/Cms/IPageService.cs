namespace FlexCms.Framework.Cms;

public interface IPageService
{
    Task<FcmsPage?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<FcmsPage?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<List<FcmsPage>> GetAllAsync(CancellationToken ct = default);
    Task<List<FcmsPage>> GetPublishedAsync(CancellationToken ct = default);
    Task<List<FcmsPage>> GetChildrenAsync(Guid? parentId, CancellationToken ct = default);
    Task<bool> SlugExistsAsync(string slug, Guid? excludeId = null, CancellationToken ct = default);
    Task<FcmsPage> CreateAsync(FcmsPage page, CancellationToken ct = default);
    Task UpdateAsync(FcmsPage page, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<List<FcmsPage>> GetDeletedAsync(CancellationToken ct = default);
    Task RestoreAsync(Guid id, CancellationToken ct = default);
    Task HardDeleteAsync(Guid id, CancellationToken ct = default);

    // ── Translations (Phase 7) ───────────────────────────────────────────────

    /// <summary>
    /// Resolve a page by language-aware slug. Lookup order:
    /// <list type="number">
    ///   <item>Translation row matching <c>(lang, slug)</c> → returns its base
    ///         <see cref="FcmsPage"/> with the translation overlaid.</item>
    ///   <item>Base page matching <c>slug</c> → returned as-is.</item>
    /// </list>
    /// Returns <c>null</c> if no match exists in either lookup.
    /// </summary>
    Task<(FcmsPage Page, FcmsPageTranslation? Translation)?> ResolveBySlugAsync(string slug, string lang, CancellationToken ct = default);

    /// <summary>List all translations for a page, ordered by language code.</summary>
    Task<List<FcmsPageTranslation>> GetTranslationsAsync(Guid pageId, CancellationToken ct = default);

    Task<FcmsPageTranslation?> GetTranslationAsync(Guid pageId, string lang, CancellationToken ct = default);

    /// <summary>Insert or update the translation for <c>(pageId, lang)</c>.</summary>
    Task<FcmsPageTranslation> SaveTranslationAsync(FcmsPageTranslation tr, CancellationToken ct = default);

    Task DeleteTranslationAsync(Guid translationId, CancellationToken ct = default);
}
