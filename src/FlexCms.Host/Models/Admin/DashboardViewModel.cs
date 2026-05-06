using FlexCms.Framework.Cms;

namespace FlexCms.Host.Models.Admin;

public class DashboardViewModel
{
    public int Pages { get; set; }
    public int Posts { get; set; }
    public int PublishedPosts { get; set; }
    public int Categories { get; set; }
    public int MediaFiles { get; set; }
    public int Users { get; set; }
    public int Roles { get; set; }
    public int PendingMessages { get; set; }
    public int FailedMessages { get; set; }
    public List<FcmsLog> RecentActivity { get; set; } = [];
    public string? AppVersion { get; set; }
    public string? Runtime { get; set; }
    public string? Os { get; set; }
}
