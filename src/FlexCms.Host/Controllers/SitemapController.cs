using FlexCms.Framework.Cms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Text;

namespace FlexCms.Host.Controllers;

[Route("sitemap.xml")]
public class SitemapController : Controller
{
    private readonly IPageService _pages;
    private readonly IPostService _posts;
    private readonly IMemoryCache _cache;

    public SitemapController(IPageService pages, IPostService posts, IMemoryCache cache)
    {
        _pages = pages;
        _posts = posts;
        _cache = cache;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var xml = await _cache.GetOrCreateAsync("sitemap_xml", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            return await BuildSitemapAsync(ct);
        });

        return Content(xml ?? "", "application/xml", Encoding.UTF8);
    }

    private async Task<string> BuildSitemapAsync(CancellationToken ct)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var pages = await _pages.GetPublishedAsync(ct);
        var posts = await _posts.GetPublishedAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");

        sb.AppendLine($"  <url><loc>{baseUrl}/</loc><changefreq>daily</changefreq><priority>1.0</priority></url>");

        foreach (var page in pages)
        {
            sb.AppendLine($"  <url>");
            sb.AppendLine($"    <loc>{baseUrl}/{page.Slug}</loc>");
            sb.AppendLine($"    <lastmod>{page.UpdatedAt:yyyy-MM-dd}</lastmod>");
            sb.AppendLine($"    <changefreq>weekly</changefreq>");
            sb.AppendLine($"    <priority>0.8</priority>");
            sb.AppendLine($"  </url>");
        }

        foreach (var post in posts)
        {
            sb.AppendLine($"  <url>");
            sb.AppendLine($"    <loc>{baseUrl}/blog/{post.Slug}</loc>");
            sb.AppendLine($"    <lastmod>{post.UpdatedAt:yyyy-MM-dd}</lastmod>");
            sb.AppendLine($"    <changefreq>monthly</changefreq>");
            sb.AppendLine($"    <priority>0.6</priority>");
            sb.AppendLine($"  </url>");
        }

        sb.AppendLine("</urlset>");
        return sb.ToString();
    }
}
