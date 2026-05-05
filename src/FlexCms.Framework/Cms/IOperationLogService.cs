namespace FlexCms.Framework.Cms;

public interface IOperationLogService
{
    Task LogAsync(
        string action,
        string entityType,
        string entityId,
        object? value = null,
        string module = "core",
        FcmsLogSeverity severity = FcmsLogSeverity.Info,
        CancellationToken ct = default);

    /// <summary>Moves all logs older than <paramref name="age"/> to the archive table.</summary>
    Task ArchiveOlderThanAsync(TimeSpan age, CancellationToken ct = default);

    /// <summary>Hard-deletes ALL records from the archive table.</summary>
    Task ClearArchiveAsync(CancellationToken ct = default);

    Task<IReadOnlyList<FcmsLog>> GetRecentAsync(int count = 100, CancellationToken ct = default);
    Task<IReadOnlyList<FcmsLogArchive>> GetArchiveAsync(int count = 100, CancellationToken ct = default);
}
