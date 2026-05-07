namespace FlexCms.Framework.Backup;

/// <summary>
/// Operator-triggered backup + restore for the framework's persistent state.
///
/// <para>
/// Backups are <b>logical, not physical</b> — the service serializes EF
/// entities to JSON inside a ZIP, alongside the media folder + the
/// <c>setup.json</c> config. This keeps the backup format portable across
/// MySQL / Postgres / SQL Server / SQLite without coupling to any
/// vendor-specific dump tool.
/// </para>
///
/// <para>
/// Restore is destructive: it expects an empty target. Use it for
/// disaster-recovery / new-environment seeding, not as a "merge older data
/// in" workflow.
/// </para>
/// </summary>
public interface IFcmsBackupService
{
    /// <summary>
    /// Build a backup ZIP and return its path on disk (under
    /// <c>App_Data/backups/{yyyy-MM-dd_HHmmss}.zip</c> by default).
    /// </summary>
    Task<BackupResult> CreateBackupAsync(BackupOptions? options = null, CancellationToken ct = default);

    /// <summary>List all backup files known to the service, newest first.</summary>
    Task<IReadOnlyList<BackupFileInfo>> ListBackupsAsync(CancellationToken ct = default);

    /// <summary>Delete the named backup file (filename only, not full path).</summary>
    Task DeleteBackupAsync(string fileName, CancellationToken ct = default);

    /// <summary>
    /// Restore from a backup file. CAUTION: this drops the existing data for
    /// every entity present in the backup before reseeding. Caller must
    /// confirm with the operator before invoking.
    /// </summary>
    Task<RestoreResult> RestoreAsync(string fileName, RestoreOptions options, CancellationToken ct = default);

    /// <summary>
    /// Apply the configured retention policy: delete backup files older than
    /// <c>SiteSettings.BackupRetentionDays</c>. Called by the scheduler.
    /// </summary>
    Task<int> ApplyRetentionAsync(int retentionDays, CancellationToken ct = default);
}

public sealed record BackupOptions(bool IncludeMedia = true, bool IncludeConfig = true);

public sealed record BackupResult(string FileName, string FilePath, long SizeBytes, int EntityCount, DateTime CreatedAt);

public sealed record BackupFileInfo(string FileName, string FilePath, long SizeBytes, DateTime CreatedAt);

public sealed record RestoreOptions(bool RestoreMedia = true, bool RestoreConfig = false);

public sealed record RestoreResult(bool Success, int EntitiesRestored, int MediaRestored, string? Error = null);
