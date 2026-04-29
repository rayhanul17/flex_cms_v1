using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Cms;

public class FcmsOperationLog : BaseEfEntity
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

    /// <summary>JSON snapshot AFTER the operation.</summary>
    public string? NewValue { get; set; }

    /// <summary>e.g. "core", "blog"</summary>
    public string Module { get; set; } = "core";

    public FcmsLogSeverity Severity { get; set; } = FcmsLogSeverity.Info;
}
