using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Cms.Comments;

public enum CommentStatus
{
    Pending = 0,
    Approved = 1,
    Spam = 2,
    Trashed = 3
}

/// <summary>
/// One comment thread row. Threading is parent-pointer (<see cref="ParentId"/>).
/// Anonymous comments capture <see cref="AuthorName"/>/<see cref="AuthorEmail"/>;
/// authenticated comments populate <see cref="AuthorUserId"/> and the
/// frontend renders the user's display name instead.
/// </summary>
public class FcmsComment : BaseEfEntity
{
    /// <summary>Type of the parent — typically <c>nameof(FcmsPost)</c>.</summary>
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }

    public Guid? ParentId { get; set; }   // null for top-level

    public Guid? AuthorUserId { get; set; }
    public string AuthorName { get; set; } = "";
    public string AuthorEmail { get; set; } = "";
    public string IpAddress { get; set; } = "";

    public string Body { get; set; } = "";

    public CommentStatus CommentStatus { get; set; } = CommentStatus.Pending;
    public Guid? ModeratedByUserId { get; set; }
    public DateTime? ModeratedAt { get; set; }

    /// <summary>Number of suspicious-pattern hits that flagged this comment as spam (debug + admin info).</summary>
    public int SpamScore { get; set; }
}
