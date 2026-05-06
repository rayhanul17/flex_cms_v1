using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Exports;
using FlexCms.Framework.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlexCms.Tests.Integration.Phase12Cleanup;

/// <summary>
/// Verifies the stale-Running reaper logic added to
/// <see cref="ExportProcessorService"/>: rows stuck in
/// <see cref="ExportStatus.Running"/> longer than the configured threshold
/// get reset to Pending so the next poll re-runs them.
/// </summary>
public sealed class StaleRunningExportReaperTests
{
    private sealed class StubStorage : IFcmsFileStorage
    {
        public Task<string> SaveAsync(string p, Stream c, CancellationToken ct = default) => Task.FromResult("/" + p);
        public Task DeleteAsync(string p, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string p, CancellationToken ct = default) => Task.FromResult(false);
    }

    private static (ServiceProvider sp, FcmsDbContext db, ExportProcessorService svc) Build(TimeSpan staleThreshold)
    {
        var dbName = Guid.NewGuid().ToString();
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddDbContext<FcmsDbContext>(o => o.UseInMemoryDatabase(dbName));
        sc.AddScoped<DbContext>(sp => sp.GetRequiredService<FcmsDbContext>());
        sc.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        sc.AddScoped<IFcmsUnitOfWork, EfUnitOfWork>();
        sc.AddSingleton<IFcmsFileStorage>(new StubStorage());
        var sp = sc.BuildServiceProvider();
#pragma warning disable CA2000
        var rootScope = sp.CreateScope();
#pragma warning restore CA2000
        var db = rootScope.ServiceProvider.GetRequiredService<FcmsDbContext>();
        var svc = new ExportProcessorService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            new ExportProcessorOptions { StaleRunningThreshold = staleThreshold },
            NullLogger<ExportProcessorService>.Instance);
        return (sp, db, svc);
    }

    [Fact]
    public async Task ReapStaleRunningAsync_resets_orphaned_rows_to_pending()
    {
        var (sp, db, svc) = Build(staleThreshold: TimeSpan.FromMinutes(30));

        // Backdate StartedAt past the threshold (45 min ago).
        var orphan = new FcmsPendingExport
        {
            HandlerId = "test",
            Format = ExportFormat.Csv,
            Title = "abandoned",
            ExportStatus = ExportStatus.Running,
            StartedAt = DateTime.UtcNow.AddMinutes(-45)
        };
        db.PendingExports.Add(orphan);
        await db.SaveChangesAsync();

        await using var scope = sp.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<FcmsPendingExport>>();
        var uow = scope.ServiceProvider.GetRequiredService<IFcmsUnitOfWork>();

        var n = await svc.ReapStaleRunningAsync(repo, uow, CancellationToken.None);

        Assert.Equal(1, n);
        var reloaded = await db.PendingExports.AsNoTracking().FirstAsync();
        Assert.Equal(ExportStatus.Pending, reloaded.ExportStatus);
        Assert.Contains("Reaped", reloaded.FailureReason!, StringComparison.Ordinal);
        sp.Dispose();
    }

    [Fact]
    public async Task ReapStaleRunningAsync_leaves_recently_started_rows_alone()
    {
        var (sp, db, svc) = Build(staleThreshold: TimeSpan.FromMinutes(30));

        // Started 5 min ago — well within threshold.
        var fresh = new FcmsPendingExport
        {
            HandlerId = "test",
            Format = ExportFormat.Csv,
            Title = "in-progress",
            ExportStatus = ExportStatus.Running,
            StartedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        db.PendingExports.Add(fresh);
        await db.SaveChangesAsync();

        await using var scope = sp.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<FcmsPendingExport>>();
        var uow = scope.ServiceProvider.GetRequiredService<IFcmsUnitOfWork>();

        var n = await svc.ReapStaleRunningAsync(repo, uow, CancellationToken.None);

        Assert.Equal(0, n);
        var reloaded = await db.PendingExports.AsNoTracking().FirstAsync();
        Assert.Equal(ExportStatus.Running, reloaded.ExportStatus);
        sp.Dispose();
    }

    [Fact]
    public async Task ReapStaleRunningAsync_leaves_done_and_failed_rows_alone()
    {
        var (sp, db, svc) = Build(staleThreshold: TimeSpan.FromMinutes(30));
        db.PendingExports.AddRange(
            new FcmsPendingExport { HandlerId = "x", Title = "d", ExportStatus = ExportStatus.Done, StartedAt = DateTime.UtcNow.AddDays(-1) },
            new FcmsPendingExport { HandlerId = "x", Title = "f", ExportStatus = ExportStatus.Failed, StartedAt = DateTime.UtcNow.AddDays(-1) });
        await db.SaveChangesAsync();

        await using var scope = sp.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<FcmsPendingExport>>();
        var uow = scope.ServiceProvider.GetRequiredService<IFcmsUnitOfWork>();

        var n = await svc.ReapStaleRunningAsync(repo, uow, CancellationToken.None);

        Assert.Equal(0, n);
        sp.Dispose();
    }
}
