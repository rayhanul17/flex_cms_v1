using FlexCms.Framework.Clock;
using FlexCms.Framework.Db;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Cms;

/// <summary>
/// Background service that runs every minute and publishes pages/posts whose
/// PublishedAt timestamp has passed but IsPublished is still false.
/// </summary>
public class ScheduledPublishService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ScheduledPublishService> _logger;

    public ScheduledPublishService(IServiceScopeFactory scopes, ILogger<ScheduledPublishService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
        {
            try { await PublishDueAsync(ct); }
            catch (Exception ex) { _logger.LogError(ex, "ScheduledPublishService tick failed."); }
        }
    }

    private async Task PublishDueAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var pageRepo = scope.ServiceProvider.GetRequiredService<Db.IRepository<FcmsPage>>();
        var postRepo = scope.ServiceProvider.GetRequiredService<Db.IRepository<FcmsPost>>();
        var uow = scope.ServiceProvider.GetRequiredService<Db.IFcmsUnitOfWork>();
        var now = FcmsTime.Now;

        var pages = await pageRepo.FindAsync(p => !p.IsPublished && p.PublishedAt != null && p.PublishedAt <= now, ct);
        var posts = await postRepo.FindAsync(p => !p.IsPublished && p.PublishedAt != null && p.PublishedAt <= now, ct);

        if (pages.Count == 0 && posts.Count == 0) return;

        foreach (var page in pages) { page.IsPublished = true; page.UpdatedAt = now; await pageRepo.UpdateAsync(page, ct); }
        foreach (var post in posts) { post.IsPublished = true; post.UpdatedAt = now; await postRepo.UpdateAsync(post, ct); }

        await uow.SaveChangesAsync(ct);
        _logger.LogInformation("ScheduledPublish: published {Pages} page(s), {Posts} post(s).", pages.Count, posts.Count);
    }
}
