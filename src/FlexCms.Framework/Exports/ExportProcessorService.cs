using FlexCms.Framework.Db;
using FlexCms.Framework.Notifications;
using FlexCms.Framework.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Exports;

public sealed class ExportProcessorOptions
{
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(30);
    public int BatchSize { get; init; } = 5;

    /// <summary>
    /// A row stuck in <see cref="ExportStatus.Running"/> longer than this is
    /// presumed orphaned (the worker that picked it up crashed before
    /// flipping it to Done/Failed). The reaper resets such rows to Pending
    /// so the next poll re-runs them. Default 1 hour.
    /// </summary>
    public TimeSpan StaleRunningThreshold { get; init; } = TimeSpan.FromHours(1);
}

/// <summary>
/// Drains <see cref="FcmsPendingExport"/> rows. Picks Pending and stale-Running
/// rows (i.e. an app crashed mid-render and left them orphaned), invokes the
/// matching <see cref="IFcmsExportHandler"/>, persists the bytes via
/// <see cref="IFcmsFileStorage"/>, and notifies the requester via
/// <see cref="IFcmsNotificationService"/> with the download URL.
/// </summary>
public sealed class ExportProcessorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ExportProcessorOptions _options;
    private readonly ILogger<ExportProcessorService> _logger;

    public ExportProcessorService(
        IServiceScopeFactory scopes,
        ExportProcessorOptions options,
        ILogger<ExportProcessorService> logger)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "ExportProcessorService poll failed"); }

            try { await Task.Delay(_options.PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Visible for tests — single-pass drain with no delay loop.</summary>
    public async Task ProcessOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var repo = sp.GetRequiredService<IRepository<FcmsPendingExport>>();
        var uow = sp.GetRequiredService<IFcmsUnitOfWork>();
        var storage = sp.GetRequiredService<IFcmsFileStorage>();
        var notifications = sp.GetService<IFcmsNotificationService>();
        var handlers = sp.GetServices<IFcmsExportHandler>().ToDictionary(h => h.HandlerId, StringComparer.OrdinalIgnoreCase);

        // Reset stale-Running rows BEFORE picking up the Pending batch — that
        // way an orphaned job (worker crashed mid-render) is rescheduled
        // alongside fresh requests on the next poll.
        await ReapStaleRunningAsync(repo, uow, ct);

        var batch = (await repo.FindAsync(e => e.ExportStatus == ExportStatus.Pending, ct))
            .OrderBy(e => e.CreatedAt)
            .Take(_options.BatchSize)
            .ToList();
        if (batch.Count == 0) return;

        foreach (var job in batch)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                if (!handlers.TryGetValue(job.HandlerId, out var handler))
                {
                    job.ExportStatus = ExportStatus.Failed;
                    job.FailureReason = $"No handler registered for '{job.HandlerId}'.";
                    await repo.UpdateAsync(job, ct);
                    continue;
                }

                job.ExportStatus = ExportStatus.Running;
                job.StartedAt = Clock.FcmsTime.Now;
                await repo.UpdateAsync(job, ct);
                await uow.SaveChangesAsync(ct);   // surface "Running" before the long-running render

                var bytes = await handler.RenderAsync(job.Format, job.ParametersJson, ct);

                var ext = job.Format switch
                {
                    ExportFormat.Excel => ".xlsx",
                    ExportFormat.Pdf => ".pdf",
                    _ => ".csv"
                };
                var name = handler.SuggestedFileName(job.Format, job.ParametersJson);
                if (string.IsNullOrWhiteSpace(name)) name = $"export-{job.Id:N}";
                if (!Path.HasExtension(name)) name += ext;

                var relativePath = $"exports/{Clock.FcmsTime.Now:yyyy}/{Clock.FcmsTime.Now:MM}/{Guid.NewGuid():N}{ext}";
                using var ms = new MemoryStream(bytes);
                var url = await storage.SaveAsync(relativePath, ms, ct);

                job.ExportStatus = ExportStatus.Done;
                job.CompletedAt = Clock.FcmsTime.Now;
                job.DownloadUrl = url;
                job.FileSizeBytes = bytes.LongLength;
                await repo.UpdateAsync(job, ct);

                if (notifications is not null && job.RequestedByUserId.HasValue)
                {
                    await notifications.NotifyUserAsync(
                        job.RequestedByUserId.Value,
                        title: $"Export ready: {job.Title}",
                        body: $"{name} ({bytes.LongLength / 1024} KB)",
                        level: NotificationLevel.Success,
                        url: url,
                        icon: "bi bi-download",
                        ct: ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Export job {Id} failed", job.Id);
                job.ExportStatus = ExportStatus.Failed;
                job.FailureReason = ex.Message;
                job.CompletedAt = Clock.FcmsTime.Now;
                await repo.UpdateAsync(job, ct);
            }
        }

        await uow.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Find rows that have been Running longer than
    /// <see cref="ExportProcessorOptions.StaleRunningThreshold"/> and flip
    /// them back to Pending. These are jobs whose worker crashed before
    /// they could be marked Done/Failed; resetting lets the next poll
    /// try them again. Public for testability.
    /// </summary>
    public async Task<int> ReapStaleRunningAsync(IRepository<FcmsPendingExport> repo, IFcmsUnitOfWork uow, CancellationToken ct)
    {
        var threshold = Clock.FcmsTime.Now - _options.StaleRunningThreshold;
        var stale = await repo.FindAsync(
            e => e.ExportStatus == ExportStatus.Running && e.StartedAt != null && e.StartedAt < threshold, ct);
        if (stale.Count == 0) return 0;

        foreach (var job in stale)
        {
            job.ExportStatus = ExportStatus.Pending;
            job.FailureReason = $"Reaped — was Running since {job.StartedAt:O}, exceeded {_options.StaleRunningThreshold} threshold.";
            await repo.UpdateAsync(job, ct);
        }
        await uow.SaveChangesAsync(ct);
        return stale.Count;
    }
}
