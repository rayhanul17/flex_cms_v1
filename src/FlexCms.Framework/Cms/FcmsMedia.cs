using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Cms;

public class FcmsMedia : BaseEfEntity
{
    public string FileName { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string MimeType { get; set; } = "";
    public string Extension { get; set; } = "";
    public long FileSize { get; set; }
    public string Url { get; set; } = "";
    public string? ThumbnailUrl { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? AltText { get; set; }
    public Guid? FolderId { get; set; }
    public FcmsMediaFolder? Folder { get; set; }
}
