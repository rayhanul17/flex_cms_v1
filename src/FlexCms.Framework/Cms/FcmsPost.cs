using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Cms;

public class FcmsPost : BaseEfEntity
{
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Excerpt { get; set; }
    public string Content { get; set; } = "";
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public string? FeaturedImageUrl { get; set; }
    public Guid? CategoryId { get; set; }
    public FcmsCategory? Category { get; set; }
    public bool IsPublished { get; set; }
    public DateTime? PublishedAt { get; set; }
    public Guid? AuthorId { get; set; }
    public int ViewCount { get; set; }
    public ICollection<FcmsPostTag> PostTags { get; set; } = [];
}
