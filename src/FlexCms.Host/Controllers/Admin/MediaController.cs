using FlexCms.Framework.Auth;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers.Admin;

[Route("admin/media")]
public class MediaController : BaseAdminController
{
    private readonly IMediaService _media;
    private readonly IMediaFolderService _folders;
    private readonly IFcmsUnitOfWork _uow;

    public MediaController(IMediaService media, IMediaFolderService folders, IFcmsUnitOfWork uow)
    {
        _media = media;
        _folders = folders;
        _uow = uow;
    }

    [HttpGet("")]
    [FcmsAuthorize(FcmsPermissions.MediaView)]
    public async Task<IActionResult> Index(Guid? folderId)
    {
        var allFolders = await _folders.GetAllAsync();
        var items = await _media.GetByFolderAsync(folderId);
        var breadcrumb = folderId.HasValue
            ? await _folders.GetBreadcrumbAsync(folderId.Value)
            : [];

        ViewBag.Folders = allFolders.Where(f => f.ParentId == folderId).ToList();
        ViewBag.CurrentFolderId = folderId;
        ViewBag.Breadcrumb = breadcrumb;
        return View(items);
    }

    [HttpPost("upload")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.MediaUpload)]
    public async Task<IActionResult> Upload(IFormFile file, Guid? folderId)
    {
        if (file is null || file.Length == 0)
            return FcmsFail("No file provided.");

        try
        {
            await _uow.BeginTransactionAsync();
            var media = await _media.UploadAsync(file, folderId);
            await _uow.SaveChangesAsync();
            await _uow.CommitAsync();
            return FcmsOk("Uploaded.", new { id = media.Id, url = media.Url, thumb = media.ThumbnailUrl, name = media.OriginalFileName });
        }
        catch (InvalidOperationException ex)
        {
            await _uow.RollbackAsync();
            return FcmsFail(ex.Message);
        }
        catch
        {
            await _uow.RollbackAsync();
            return FcmsFail("Upload failed.");
        }
    }

    [HttpPost("folder/create")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.MediaFolders)]
    public async Task<IActionResult> CreateFolder(string name, Guid? parentId)
    {
        if (string.IsNullOrWhiteSpace(name))
            return FcmsFail("Folder name is required.");

        try
        {
            await _uow.BeginTransactionAsync();
            var folder = await _folders.CreateAsync(name, parentId);
            await _uow.SaveChangesAsync();
            await _uow.CommitAsync();
            return FcmsOk("Folder created.", new { id = folder.Id, name = folder.Name });
        }
        catch
        {
            await _uow.RollbackAsync();
            return FcmsFail("Failed to create folder.");
        }
    }

    [HttpPost("folder/{id:guid}/rename")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.MediaFolders)]
    public async Task<IActionResult> RenameFolder(Guid id, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return FcmsFail("Name is required.");

        try
        {
            await _uow.BeginTransactionAsync();
            var folder = await _folders.RenameAsync(id, name);
            await _uow.SaveChangesAsync();
            await _uow.CommitAsync();
            return FcmsOk("Renamed.", new { name = folder.Name });
        }
        catch (InvalidOperationException ex)
        {
            await _uow.RollbackAsync();
            return FcmsFail(ex.Message);
        }
        catch
        {
            await _uow.RollbackAsync();
            return FcmsFail("Failed to rename folder.");
        }
    }

    [HttpPost("folder/{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.MediaFolders)]
    public async Task<IActionResult> DeleteFolder(Guid id)
    {
        try
        {
            await _uow.BeginTransactionAsync();
            await _folders.DeleteAsync(id);
            await _uow.SaveChangesAsync();
            await _uow.CommitAsync();
            return FcmsOk("Folder deleted.");
        }
        catch (InvalidOperationException ex)
        {
            await _uow.RollbackAsync();
            return FcmsFail(ex.Message);
        }
        catch
        {
            await _uow.RollbackAsync();
            return FcmsFail("Failed to delete folder.");
        }
    }

    [HttpPost("{id:guid}/move")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.MediaEdit)]
    public async Task<IActionResult> Move(Guid id, Guid? targetFolderId)
    {
        try
        {
            await _uow.BeginTransactionAsync();
            await _media.MoveToFolderAsync(id, targetFolderId);
            await _uow.SaveChangesAsync();
            await _uow.CommitAsync();
            return FcmsOk("Moved.");
        }
        catch (InvalidOperationException ex)
        {
            await _uow.RollbackAsync();
            return FcmsFail(ex.Message);
        }
        catch
        {
            await _uow.RollbackAsync();
            return FcmsFail("Failed to move media.");
        }
    }

    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.MediaDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            await _uow.BeginTransactionAsync();
            await _media.SoftDeleteAsync(id);
            await _uow.SaveChangesAsync();
            await _uow.CommitAsync();
            ShowSuccess("Media deleted.");
        }
        catch (InvalidOperationException ex)
        {
            await _uow.RollbackAsync();
            ShowError(ex.Message);
        }
        catch
        {
            await _uow.RollbackAsync();
            ShowError("Failed to delete media.");
        }
        return RedirectToAction(nameof(Index));
    }

    // ── Bulk alt-text editor ─────────────────────────────────────────────────
    // Accessibility (WCAG 2.1 AA) + image-search SEO both depend on alt text.
    // Editing one media item at a time is fine for new uploads but useless
    // for bringing legacy libraries into compliance — typical sites import
    // 50-500 images at a time, and per-image edit takes 30s each.

    [HttpGet("alt-text")]
    [FcmsAuthorize(FcmsPermissions.MediaEdit)]
    public async Task<IActionResult> AltText(Guid? folderId, bool missingOnly = false)
    {
        var allFolders = await _folders.GetAllAsync();
        var items = await _media.GetByFolderAsync(folderId);

        // Default to images-only — alt text is only meaningful for visual
        // media. Audio / PDF / video have their own accessibility metadata.
        items = items
            .Where(m => m.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (missingOnly)
            items = items.Where(m => string.IsNullOrWhiteSpace(m.AltText)).ToList();

        ViewBag.Folders = allFolders;
        ViewBag.CurrentFolderId = folderId;
        ViewBag.MissingOnly = missingOnly;
        ViewBag.MissingCount = items.Count(m => string.IsNullOrWhiteSpace(m.AltText));
        return View(items);
    }

    [HttpPost("alt-text/bulk-save")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.MediaEdit)]
    public async Task<IActionResult> AltTextBulkSave([FromForm] Dictionary<Guid, string?> alt)
    {
        if (alt is null || alt.Count == 0)
            return FcmsFail("Nothing to save.");
        var n = await _media.BulkUpdateAltTextAsync(alt);
        return FcmsOk($"Updated {n} item(s).", new { count = n });
    }
}
