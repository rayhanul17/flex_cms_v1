using FlexCms.Framework.Db;

namespace FlexCms.Framework.Cms.Revisions;

public interface IContentRevisionService
{
    /// <summary>Append a new revision row; auto-numbers Version = max+1 for the entity.</summary>
    Task<FcmsContentRevision> SnapshotAsync(string entityType, Guid entityId, string title, string contentSnapshot, Guid? authorUserId = null, string? comment = null, CancellationToken ct = default);

    Task<List<FcmsContentRevision>> GetForAsync(string entityType, Guid entityId, CancellationToken ct = default);

    Task<FcmsContentRevision?> GetAsync(Guid revisionId, CancellationToken ct = default);
}

public sealed class ContentRevisionService : IContentRevisionService
{
    private readonly IRepository<FcmsContentRevision> _repo;
    private readonly IFcmsUnitOfWork _uow;

    public ContentRevisionService(IRepository<FcmsContentRevision> repo, IFcmsUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<FcmsContentRevision> SnapshotAsync(string entityType, Guid entityId, string title, string contentSnapshot, Guid? authorUserId = null, string? comment = null, CancellationToken ct = default)
    {
        var existing = await _repo.FindAsync(r => r.EntityType == entityType && r.EntityId == entityId, ct);
        var nextVersion = existing.Count == 0 ? 1 : existing.Max(r => r.Version) + 1;

        var rev = new FcmsContentRevision
        {
            EntityType = entityType ?? "",
            EntityId = entityId,
            Version = nextVersion,
            Title = title ?? "",
            ContentSnapshot = contentSnapshot ?? "",
            AuthorUserId = authorUserId,
            Comment = comment
        };
        await _repo.AddAsync(rev, ct);
        await _uow.SaveChangesAsync(ct);
        return rev;
    }

    public async Task<List<FcmsContentRevision>> GetForAsync(string entityType, Guid entityId, CancellationToken ct = default)
    {
        var rows = await _repo.FindAsync(r => r.EntityType == entityType && r.EntityId == entityId, ct);
        return rows.OrderByDescending(r => r.Version).ToList();
    }

    public Task<FcmsContentRevision?> GetAsync(Guid revisionId, CancellationToken ct = default)
        => _repo.GetByIdAsync(revisionId, ct);
}
