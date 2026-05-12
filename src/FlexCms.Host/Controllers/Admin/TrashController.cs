using FlexCms.Framework.Auth;
using FlexCms.Framework.Cms;
using FlexCms.Host.Models.Admin;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers.Admin;

[FcmsAuthorize(FcmsPermissions.PostsEdit)]
[Route("admin/trash")]
public class TrashController : BaseAdminController
{
    private readonly IPageService _pages;
    private readonly IPostService _posts;

    public TrashController(IPageService pages, IPostService posts)
    {
        _pages = pages;
        _posts = posts;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var pages = await _pages.GetDeletedAsync(ct);
        var posts = await _posts.GetDeletedAsync(ct);

        var vm = new TrashViewModel
        {
            Pages = pages.Select(p => new TrashItemViewModel
            {
                Id = p.Id,
                Title = p.Title,
                Type = "Page",
                DeletedAt = p.DeletedAt
            }).ToList(),
            Posts = posts.Select(p => new TrashItemViewModel
            {
                Id = p.Id,
                Title = p.Title,
                Type = "Post",
                DeletedAt = p.DeletedAt
            }).ToList()
        };

        return View(vm);
    }

    [HttpPost("pages/{id:guid}/restore")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.PagesEdit)]
    public async Task<IActionResult> RestorePage(Guid id, CancellationToken ct)
    {
        await _pages.RestoreAsync(id, ct);
        return FcmsOk("Page restored.");
    }

    [HttpPost("pages/{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.PagesDelete)]
    public async Task<IActionResult> DeletePage(Guid id, CancellationToken ct)
    {
        await _pages.HardDeleteAsync(id, ct);
        return FcmsOk("Page permanently deleted.");
    }

    [HttpPost("posts/{id:guid}/restore")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.PostsEdit)]
    public async Task<IActionResult> RestorePost(Guid id, CancellationToken ct)
    {
        await _posts.RestoreAsync(id, ct);
        return FcmsOk("Post restored.");
    }

    [HttpPost("posts/{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.PostsDelete)]
    public async Task<IActionResult> DeletePost(Guid id, CancellationToken ct)
    {
        await _posts.HardDeleteAsync(id, ct);
        return FcmsOk("Post permanently deleted.");
    }
}
