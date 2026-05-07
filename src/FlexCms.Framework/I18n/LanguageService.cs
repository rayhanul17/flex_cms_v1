using FlexCms.Framework.Db;

namespace FlexCms.Framework.I18n;

public sealed class LanguageService : ILanguageService
{
    private readonly IRepository<FcmsLanguage> _repo;
    private readonly IFcmsUnitOfWork _uow;

    public LanguageService(IRepository<FcmsLanguage> repo, IFcmsUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<IReadOnlyList<FcmsLanguage>> ListAllAsync(CancellationToken ct = default)
    {
        var rows = await _repo.GetAllAsync(ct);
        return rows.OrderBy(r => r.SortOrder).ThenBy(r => r.Code).ToList();
    }

    public async Task<IReadOnlyList<FcmsLanguage>> ListActiveAsync(CancellationToken ct = default)
    {
        var rows = await _repo.FindAsync(r => r.IsActive, ct);
        return rows.OrderBy(r => r.SortOrder).ThenBy(r => r.Code).ToList();
    }

    public async Task<FcmsLanguage?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var rows = await _repo.GetAllAsync(ct);
        return rows.FirstOrDefault(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    public async Task UpsertAsync(FcmsLanguage language, CancellationToken ct = default)
    {
        if (language is null) throw new ArgumentNullException(nameof(language));
        if (string.IsNullOrWhiteSpace(language.Code))
            throw new ArgumentException("Code required.", nameof(language));

        var existing = await GetByCodeAsync(language.Code, ct);
        if (existing is null)
        {
            await _repo.AddAsync(language, ct);
        }
        else
        {
            existing.DisplayName = language.DisplayName;
            existing.IsRtl = language.IsRtl;
            existing.IsActive = language.IsActive;
            existing.SortOrder = language.SortOrder;
            await _repo.UpdateAsync(existing, ct);
        }
        await _uow.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string code, CancellationToken ct = default)
    {
        var existing = await GetByCodeAsync(code, ct);
        if (existing is null) return;
        await _repo.DeleteAsync(existing, ct);
        await _uow.SaveChangesAsync(ct);
    }
}
