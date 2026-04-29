using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Cms;

public class FcmsTag : BaseEfEntity
{
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public ICollection<FcmsPostTag> PostTags { get; set; } = [];
}
