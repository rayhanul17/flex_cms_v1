using FlexCms.Framework.Clock;
using FlexCms.Framework.Db;

namespace FlexCms.Framework.Cms.Drafts;

public sealed class DraftSnapshotService : IDraftSnapshotService
{
    private readonly IRepository<FcmsContentDraftSnapshot> _repo;
    private readonly IFcmsUnitOfWork _uow;

    public DraftSnapshotService(IRepository<FcmsContentDraftSnapshot> repo, IFcmsUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task SaveAsync(string entityType, Guid entityId, Guid userId, DraftSnapshotPayload payload, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(entityType)) return;
        var existing = await GetAsync(entityType, entityId, userId, ct);
        if (existing is null)
        {
            await _repo.AddAsync(new FcmsContentDraftSnapshot
            {
                EntityType = entityType,
                EntityId = entityId,
                UserId = userId,
                Title = payload.Title,
                Content = payload.Content,
                Excerpt = payload.Excerpt,
                CapturedAt = FcmsTime.Now,
            }, ct);
        }
        else
        {
            existing.Title = payload.Title;
            existing.Content = payload.Content;
            existing.Excerpt = payload.Excerpt;
            existing.CapturedAt = FcmsTime.Now;
            await _repo.UpdateAsync(existing, ct);
        }
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<FcmsContentDraftSnapshot?> GetAsync(string entityType, Guid entityId, Guid userId, CancellationToken ct = default)
    {
        var rows = await _repo.FindAsync(s =>
            s.EntityType == entityType &&
            s.EntityId == entityId &&
            s.UserId == userId, ct);
        return rows.FirstOrDefault();
    }

    public async Task DiscardAsync(string entityType, Guid entityId, Guid userId, CancellationToken ct = default)
    {
        var existing = await GetAsync(entityType, entityId, userId, ct);
        if (existing is null) return;
        await _repo.DeleteAsync(existing, ct);
        await _uow.SaveChangesAsync(ct);
    }
}
