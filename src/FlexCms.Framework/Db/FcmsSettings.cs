using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Db;

public class FcmsSettings : BaseEfEntity
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";   // JSON-serialized typed settings
    public string? ModuleId { get; set; }     // null = core/site settings
}
