using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Cms;

public enum PageAccessControl
{
    Public = 0,
    AuthenticatedOnly = 1,
    PasswordProtected = 2
}

public class FcmsPage : BaseEfEntity
{
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Content { get; set; } = "";
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public Guid? ParentId { get; set; }
    public FcmsPage? Parent { get; set; }
    public ICollection<FcmsPage> Children { get; set; } = [];
    public int SortOrder { get; set; }
    public bool IsPublished { get; set; }
    public DateTime? PublishedAt { get; set; }
    public Guid? AuthorId { get; set; }
    public PageAccessControl AccessControl { get; set; } = PageAccessControl.Public;
    public string? PasswordHash { get; set; }

    public ICollection<FcmsPageTranslation> Translations { get; set; } = [];
}
