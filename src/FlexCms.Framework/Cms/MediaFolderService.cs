using FlexCms.Framework.Db;

namespace FlexCms.Framework.Cms;

public class MediaFolderService : IMediaFolderService
{
    private readonly IRepository<FcmsMediaFolder> _folderRepo;
    private readonly IRepository<FcmsMedia> _mediaRepo;

    public MediaFolderService(IRepository<FcmsMediaFolder> folderRepo, IRepository<FcmsMedia> mediaRepo)
    {
        _folderRepo = folderRepo;
        _mediaRepo = mediaRepo;
    }

    public async Task<FcmsMediaFolder> CreateAsync(string name, Guid? parentId, CancellationToken ct = default)
    {
        var folder = new FcmsMediaFolder { Name = name.Trim(), ParentId = parentId };
        await _folderRepo.AddAsync(folder, ct);
        return folder;
    }

    public async Task<FcmsMediaFolder> RenameAsync(Guid id, string newName, CancellationToken ct = default)
    {
        var folder = await _folderRepo.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("Folder not found.");
        folder.Name = newName.Trim();
        await _folderRepo.UpdateAsync(folder, ct);
        return folder;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        // Move media in this folder to parent (or root) before deleting
        var all = await _mediaRepo.GetAllAsync(ct);
        var folder = await _folderRepo.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("Folder not found.");

        foreach (var m in all.Where(m => m.FolderId == id))
        {
            m.FolderId = folder.ParentId;
            await _mediaRepo.UpdateAsync(m, ct);
        }

        await _folderRepo.SoftDeleteAsync(folder, ct);
    }

    public async Task<IReadOnlyList<FcmsMediaFolder>> GetAllAsync(CancellationToken ct = default)
    {
        var all = await _folderRepo.GetAllAsync(ct);
        return all.ToList();
    }

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
