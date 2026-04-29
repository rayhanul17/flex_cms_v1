using FlexCms.Framework.Cms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Text;

namespace FlexCms.Host.Controllers;

[Route("rss")]
public class RssController : Controller
{
    private readonly IPostService _posts;
    private readonly IMemoryCache _cache;

    public RssController(IPostService posts, IMemoryCache cache)
    {
        _posts = posts;
        _cache = cache;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var xml = await _cache.GetOrCreateAsync("rss_xml", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            return await BuildRssAsync(ct);
        });

        return Content(xml ?? "", "application/rss+xml", Encoding.UTF8);
    }

    private async Task<string> BuildRssAsync(CancellationToken ct)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var posts = (await _posts.GetPublishedAsync(ct)).Take(50).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<rss version=\"2.0\" xmlns:atom=\"http://www.w3.org/2005/Atom\">");
        sb.AppendLine("  <channel>");
        sb.AppendLine($"    <title>Blog</title>");
        sb.AppendLine($"    <link>{baseUrl}/blog</link>");
        sb.AppendLine($"    <description>Latest posts</description>");
        sb.AppendLine($"    <atom:link href=\"{baseUrl}/rss\" rel=\"self\" type=\"application/rss+xml\" />");

        foreach (var post in posts)
        {
            var pubDate = (post.PublishedAt ?? post.CreatedAt).ToString("R");
            var description = string.IsNullOrEmpty(post.Excerpt) ? "" : EscapeXml(post.Excerpt);
            sb.AppendLine("    <item>");
            sb.AppendLine($"      <title>{EscapeXml(post.Title)}</title>");
            sb.AppendLine($"      <link>{baseUrl}/blog/{post.Slug}</link>");
            sb.AppendLine($"      <guid isPermaLink=\"true\">{baseUrl}/blog/{post.Slug}</guid>");
            sb.AppendLine($"      <pubDate>{pubDate}</pubDate>");
            sb.AppendLine($"      <description>{description}</description>");
            sb.AppendLine("    </item>");
        }

        sb.AppendLine("  </channel>");
        sb.AppendLine("</rss>");
        return sb.ToString();
    }

    private static string EscapeXml(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
