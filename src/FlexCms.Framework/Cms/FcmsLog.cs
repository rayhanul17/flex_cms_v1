using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Cms;

/// <summary>
/// Append-only audit log entry. Inherits <see cref="BaseEfEntity"/> only
/// for <see cref="IRepository{T}"/> compatibility — the lifecycle / soft-delete
/// columns (Status, DeletedAt, UpdatedAt, UpdatedBy, CreatedBy) are
/// explicitly ignored in <c>FcmsDbContext.OnModelCreating</c> because logs
/// are never updated or soft-deleted. The <see cref="Db.IAppendOnlyEntity"/>
/// marker excludes them from the global Status soft-delete filter.
/// </summary>
public class FcmsLog : BaseEfEntity, Db.IAppendOnlyEntity
{
    public Guid? UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserIp { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;

    /// <summary>e.g. "Post.Created", "User.Deleted", "Settings.Updated"</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>e.g. "FcmsPost"</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Stored as string to handle both Guid and int PKs.</summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>JSON snapshot of the entity AFTER the operation (current value).</summary>
    public string? Value { get; set; }

    /// <summary>e.g. "core", "blog"</summary>
    public string Module { get; set; } = "core";

    public FcmsLogSeverity Severity { get; set; } = FcmsLogSeverity.Info;
}
