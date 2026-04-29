using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Cms;

public class FcmsRedirect : BaseEfEntity
{
    /// <summary>Incoming path, e.g. "/old-about-us"</summary>
    public string FromPath { get; set; } = "";

    /// <summary>Target path or absolute URL, e.g. "/about" or "https://example.com"</summary>
    public string ToPath { get; set; } = "";

    /// <summary>301 (permanent) or 302 (temporary)</summary>
    public int StatusCode { get; set; } = 301;

    public bool IsActive { get; set; } = true;
    public int HitCount { get; set; }
}
