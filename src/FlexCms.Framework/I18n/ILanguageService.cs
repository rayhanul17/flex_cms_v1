namespace FlexCms.Framework.I18n;

/// <summary>
/// Read/write the admin-managed language list (Phase 15 — Issue 98).
/// </summary>
public interface ILanguageService
{
    /// <summary>All languages in admin order. Includes inactive (hidden from public switcher).</summary>
    Task<IReadOnlyList<FcmsLanguage>> ListAllAsync(CancellationToken ct = default);

    /// <summary>Active languages only — for the public language switcher.</summary>
    Task<IReadOnlyList<FcmsLanguage>> ListActiveAsync(CancellationToken ct = default);

    Task<FcmsLanguage?> GetByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>Upsert by <see cref="FcmsLanguage.Code"/>.</summary>
    Task UpsertAsync(FcmsLanguage language, CancellationToken ct = default);

    Task DeleteAsync(string code, CancellationToken ct = default);
}
