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
    /// <summary>
    /// Optional auto-unpublish timestamp. When set + reached, the
    /// <c>ScheduledPublishService</c> flips <see cref="IsPublished"/> to false
    /// (Phase 15). Independent from soft-delete.
    /// </summary>
    public DateTime? UnpublishAt { get; set; }
    public Guid? AuthorId { get; set; }
    public PageAccessControl AccessControl { get; set; } = PageAccessControl.Public;
    public string? PasswordHash { get; set; }

    /// <summary>
    /// Optimistic-concurrency token (Phase 15 — Issue 96). EF will increment
    /// this on every update; the editor sends back the value it loaded with,
    /// and a mismatch on save is treated as "another editor saved first".
    /// </summary>
    public byte[]? RowVersion { get; set; }

    public ICollection<FcmsPageTranslation> Translations { get; set; } = [];
}
