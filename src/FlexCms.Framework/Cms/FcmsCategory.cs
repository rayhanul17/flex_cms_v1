using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Cms;

public class FcmsCategory : BaseEfEntity
{
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Description { get; set; }
    public Guid? ParentId { get; set; }
    public FcmsCategory? Parent { get; set; }
    public ICollection<FcmsCategory> Children { get; set; } = [];
    public int SortOrder { get; set; }
    public ICollection<FcmsPost> Posts { get; set; } = [];
}
