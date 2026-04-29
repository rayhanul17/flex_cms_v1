using FlexCms.Framework.Clock;
using FlexCms.Framework.Db;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Cms;

public class TrashCleanupOptions
{
    public int RetentionDays { get; init; } = 30;
}

/// <summary>
/// Permanently deletes pages and posts that have been in the trash longer than
/// <see cref="TrashCleanupOptions.RetentionDays"/> days. Runs once every 24 hours.
/// </summary>
public class TrashCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly TrashCleanupOptions _opts;
    private readonly ILogger<TrashCleanupService> _logger;

    public TrashCleanupService(IServiceScopeFactory scopes, TrashCleanupOptions opts, ILogger<TrashCleanupService> logger)
    {
        _scopes = scopes;
        _opts = opts;
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
        var pageRepo = scope.ServiceProvider.GetRequiredService<Db.IRepository<FcmsPage>>();
        var postRepo = scope.ServiceProvider.GetRequiredService<Db.IRepository<FcmsPost>>();
        var postTagRepo = scope.ServiceProvider.GetRequiredService<Db.IRepository<FcmsPostTag>>();
        var cutoff = FcmsTime.Now.AddDays(-_opts.RetentionDays);

        var oldPages = await pageRepo.FindAsync(p => p.IsDeleted && p.DeletedAt != null && p.DeletedAt < cutoff, ct: ct, includeDeleted: true);
        var oldPosts = await postRepo.FindAsync(p => p.IsDeleted && p.DeletedAt != null && p.DeletedAt < cutoff, ct: ct, includeDeleted: true);

        foreach (var page in oldPages) await pageRepo.DeleteAsync(page, ct);

        foreach (var post in oldPosts)
        {
            var tags = await postTagRepo.FindAsync(pt => pt.PostId == post.Id, ct: ct, includeDeleted: true);
            foreach (var tag in tags) await postTagRepo.DeleteAsync(tag, ct);
            await postRepo.DeleteAsync(post, ct);
        }

        if (oldPages.Count + oldPosts.Count > 0)
        {
            _logger.LogInformation("TrashCleanup: purged {Pages} page(s), {Posts} post(s).", oldPages.Count, oldPosts.Count);
        }
    }
}
