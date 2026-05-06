using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Cms;

public class FcmsPostTag : BaseEfEntity
{
    public Guid PostId { get; set; }
    public FcmsPost Post { get; set; } = null!;
    public Guid TagId { get; set; }
    public FcmsTag Tag { get; set; } = null!;
}
