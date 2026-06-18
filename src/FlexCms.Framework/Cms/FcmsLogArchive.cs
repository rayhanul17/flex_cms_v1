using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Cms;

/// <summary>
/// Archive table — old logs are moved here by <c>FcmsLogService.ArchiveOlderThanAsync</c>.
/// Admin may hard-delete the entire archive via the admin UI. No auto-delete.
///
/// Like <see cref="FcmsLog"/>, the lifecycle / soft-delete columns inherited
/// from <see cref="BaseEfEntity"/> are ignored in <c>FcmsDbContext.OnModelCreating</c>
/// because archive entries are append-only or hard-deleted in bulk.
/// </summary>
public class FcmsLogArchive : BaseEfEntity, Db.IAppendOnlyEntity
{
    public Guid? UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserIp { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? Value { get; set; }
    public string Module { get; set; } = "core";
    public FcmsLogSeverity Severity { get; set; } = FcmsLogSeverity.Info;

    /// <summary>Copied from <see cref="FcmsLog.PrevHash"/> when the row was archived.</summary>
    public string? PrevHash { get; set; }

    /// <summary>Copied from <see cref="FcmsLog.Hash"/> when the row was archived.</summary>
    public string? Hash { get; set; }
}
