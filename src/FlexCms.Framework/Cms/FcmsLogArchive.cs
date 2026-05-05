using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Cms;

/// <summary>
/// Archive table — old logs are moved here by the background archiver.
/// Admin may hard-delete the entire archive via the admin UI. No auto-delete.
/// </summary>
public class FcmsLogArchive : BaseEfEntity
{
    public Guid? UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserIp { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? NewValue { get; set; }
    public string Module { get; set; } = "core";
    public FcmsLogSeverity Severity { get; set; } = FcmsLogSeverity.Info;
}
