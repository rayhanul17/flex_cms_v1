using FlexCms.Framework.Clock;
using FlexCms.Framework.Db.Ef;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Cms;

/// <summary>
/// Permanently deletes pages and posts that have been in the trash for more than 30 days.
/// Runs once every 24 hours.
/// </summary>
public class TrashCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<TrashCleanupService> _logger;

    public TrashCleanupService(IServiceScopeFactory scopes, ILogger<TrashCleanupService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
        {
            try { await PurgeOldTrashAsync(ct); }
            catch (Exception ex) { _logger.LogError(ex, "TrashCleanupService failed."); }
        }
    }

    private async Task PurgeOldTrashAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FcmsDbContext>();
        var cutoff = FcmsTime.Now.AddDays(-30);

        var oldPages = await db.Pages
            .IgnoreQueryFilters()
            .Where(p => p.IsDeleted && p.DeletedAt != null && p.DeletedAt < cutoff)
            .ToListAsync(ct);

        var oldPosts = await db.Posts
            .IgnoreQueryFilters()
            .Where(p => p.IsDeleted && p.DeletedAt != null && p.DeletedAt < cutoff)
            .ToListAsync(ct);

        if (oldPages.Count > 0) db.Pages.RemoveRange(oldPages);

        if (oldPosts.Count > 0)
        {
            var postIds = oldPosts.Select(p => p.Id).ToList();
            var tags = await db.PostTags.Where(pt => postIds.Contains(pt.PostId)).ToListAsync(ct);
            db.PostTags.RemoveRange(tags);
            db.Posts.RemoveRange(oldPosts);
        }

        if (oldPages.Count + oldPosts.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("TrashCleanup: purged {Pages} page(s), {Posts} post(s).", oldPages.Count, oldPosts.Count);
        }
    }
}
