namespace FlexCms.Framework.Cms;

public interface IOperationLogService
{
    Task LogAsync(
        string action,
        string entityType,
        string entityId,
        object? newValue = null,
        string module = "core",
        FcmsLogSeverity severity = FcmsLogSeverity.Info,
        CancellationToken ct = default);

    /// <summary>Moves all logs older than <paramref name="age"/> to the archive table.</summary>
    Task ArchiveOlderThanAsync(TimeSpan age, CancellationToken ct = default);

    /// <summary>Hard-deletes ALL records from the archive table.</summary>
    Task ClearArchiveAsync(CancellationToken ct = default);

    Task<IReadOnlyList<FcmsOperationLog>> GetRecentAsync(int count = 100, CancellationToken ct = default);
    Task<IReadOnlyList<FcmsOperationLogArchive>> GetArchiveAsync(int count = 100, CancellationToken ct = default);
}
