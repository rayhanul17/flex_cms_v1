using FlexCms.Framework.Db;

namespace FlexCms.Framework.Cms;

public class MediaFolderService : IMediaFolderService
{
    private readonly IRepository<FcmsMediaFolder> _folderRepo;
    private readonly IRepository<FcmsMedia> _mediaRepo;
    private readonly IFcmsUnitOfWork _uow;
    private readonly IOperationLogService _audit;

    public MediaFolderService(
        IRepository<FcmsMediaFolder> folderRepo,
        IRepository<FcmsMedia> mediaRepo,
        IFcmsUnitOfWork uow,
        IOperationLogService audit)
    {
        _folderRepo = folderRepo;
        _mediaRepo = mediaRepo;
        _uow = uow;
        _audit = audit;
    }

    public async Task<FcmsMediaFolder> CreateAsync(string name, Guid? parentId, CancellationToken ct = default)
    {
        var folder = new FcmsMediaFolder { Name = name.Trim(), ParentId = parentId };
        await _folderRepo.AddAsync(folder, ct);
        await _uow.SaveChangesAsync(ct);
        await _audit.LogAsync(FcmsAuditActions.FolderCreated, nameof(FcmsMediaFolder), folder.Id.ToString(),
            new { folder.Name, ParentId = parentId }, ct: ct);
        return folder;
    }

    public async Task<FcmsMediaFolder> RenameAsync(Guid id, string newName, CancellationToken ct = default)
    {
        var folder = await _folderRepo.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("Folder not found.");
        var oldName = folder.Name;
        folder.Name = newName.Trim();
        await _folderRepo.UpdateAsync(folder, ct);
        await _uow.SaveChangesAsync(ct);
        await _audit.LogAsync(FcmsAuditActions.FolderRenamed, nameof(FcmsMediaFolder), id.ToString(),
            new { OldName = oldName, NewName = folder.Name }, ct: ct);
        return folder;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var folder = await _folderRepo.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("Folder not found.");

        // Reparent media in this folder before deleting
        var affected = await _mediaRepo.FindAsync(m => m.FolderId == id, ct);
        if (affected.Count > 0)
        {
            foreach (var m in affected)
                m.FolderId = folder.ParentId;
            await _mediaRepo.UpdateRangeAsync(affected, ct);
        }

        await _audit.LogAsync(FcmsAuditActions.FolderDeleted, nameof(FcmsMediaFolder), id.ToString(),
            new { folder.Name }, ct: ct);
        await _folderRepo.SoftDeleteAsync(folder, ct);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<FcmsMediaFolder>> GetAllAsync(CancellationToken ct = default)
        => await _folderRepo.GetAllAsync(ct);

    public async Task<IReadOnlyList<FcmsMediaFolder>> GetBreadcrumbAsync(Guid folderId, CancellationToken ct = default)
    {
        var all = await _folderRepo.GetAllAsync(ct);
        var map = all.ToDictionary(f => f.Id);

        var crumb = new List<FcmsMediaFolder>();
        if (!map.TryGetValue(folderId, out var current)) return crumb;

        while (current is not null)
        {
            crumb.Insert(0, current);
            current = current.ParentId.HasValue && map.TryGetValue(current.ParentId.Value, out var p) ? p : null;
        }

        return crumb;
    }
}
