using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Cms;

public class FcmsMediaFolder : BaseEfEntity
{
    public string Name { get; set; } = "";
    public Guid? ParentId { get; set; }
    public FcmsMediaFolder? Parent { get; set; }
    public ICollection<FcmsMediaFolder> Children { get; set; } = [];
    public ICollection<FcmsMedia> Media { get; set; } = [];
}
