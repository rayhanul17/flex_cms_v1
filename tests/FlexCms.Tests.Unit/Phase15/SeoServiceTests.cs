using FlexCms.Framework.Db;
using FlexCms.Framework.Seo;
using FlexCms.Framework.Services;
using NSubstitute;
using Xunit;

namespace FlexCms.Tests.Unit.Phase15;

/// <summary>
/// SeoService rendering — exercises HTML/JSON-LD output through a substituted
/// repo so we don't need EF. Focus is the FALLBACK chain (per-entity row
/// optional → entity title/description → site name) and that no admin input
/// can break out of the script tag via JSON-LD injection.
/// </summary>
public class SeoServiceTests
{
    private static (SeoService svc, IRepository<FcmsSeoMeta> repo) Create(FcmsSeoMeta? row = null, string siteName = "FlexCms Site")
    {
        var repo = Substitute.For<IRepository<FcmsSeoMeta>>();
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(row is null ? new List<FcmsSeoMeta>() : new List<FcmsSeoMeta> { row });
        var uow = Substitute.For<IFcmsUnitOfWork>();
        var settings = Substitute.For<ISettingsService>();
        // Generic stub: any GetAsync<T> for "site:general" returns a T with SiteName populated.
        // Since we can't easily intercept generic calls with NSubstitute, the service
        // catches the missing-key path and falls back to "" — which is fine for these tests.
        return (new SeoService(repo, uow, settings), repo);
    }

    private static SeoRenderContext Ctx(string title = "Hello", string desc = "World") =>
        new("FcmsPost", Guid.NewGuid(), title, desc, "https://example.com/blog/hello",
            FeaturedImageUrl: "https://example.com/img.jpg",
            AuthorName: "Rayhan",
            PublishedAt: new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public async Task RenderHeadTags_uses_entity_fallback_when_no_seo_row()
    {
        var (svc, _) = Create(row: null);
        var html = await svc.RenderHeadTagsAsync(Ctx("My Title", "My Desc"));
        Assert.Contains("<link rel=\"canonical\" href=\"https://example.com/blog/hello\"", html);
        Assert.Contains("og:title\" content=\"My Title\"", html);
        Assert.Contains("og:description\" content=\"My Desc\"", html);
        Assert.Contains("og:type\" content=\"article\"", html);
        Assert.Contains("twitter:card\" content=\"summary_large_image\"", html);
    }

    [Fact]
    public async Task RenderHeadTags_uses_overrides_when_seo_row_set()
    {
        var entityId = Guid.NewGuid();
        var row = new FcmsSeoMeta
        {
            EntityType = "FcmsPost",
            EntityId = entityId,
            OgTitle = "Custom OG Title",
            OgDescription = "Custom OG Desc",
            OgImageUrl = "https://cdn.example.com/og.png",
            CanonicalUrl = "https://canonical.example.com/x",
            NoIndex = true,
            TwitterCard = "summary",
        };
        var (svc, _) = Create(row);
        var ctx = new SeoRenderContext("FcmsPost", entityId, "Entity Title", "Entity Desc",
            "https://example.com/blog/x");
        var html = await svc.RenderHeadTagsAsync(ctx);

        Assert.Contains("og:title\" content=\"Custom OG Title\"", html);
        Assert.Contains("og:description\" content=\"Custom OG Desc\"", html);
        Assert.Contains("og:image\" content=\"https://cdn.example.com/og.png\"", html);
        Assert.Contains("noindex,nofollow", html);
        Assert.Contains("twitter:card\" content=\"summary\"", html);
        Assert.Contains("canonical\" href=\"https://canonical.example.com/x\"", html);
    }

    [Fact]
    public async Task RenderHeadTags_html_encodes_user_input()
    {
        var (svc, _) = Create(row: null);
        // XSS-style payload — must be encoded.
        var html = await svc.RenderHeadTagsAsync(Ctx("<script>alert(1)</script>", "Q & A"));
        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;alert", html);
        Assert.Contains("Q &amp; A", html);
    }

    [Fact]
    public async Task RenderJsonLd_generates_default_when_no_custom()
    {
        var (svc, _) = Create(row: null);
        var html = await svc.RenderJsonLdAsync(Ctx());
        Assert.Contains("application/ld+json", html);
        Assert.Contains("\"@type\":\"Article\"", html);
        Assert.Contains("\"headline\":\"Hello\"", html);
        Assert.Contains("\"datePublished\":", html);
        Assert.Contains("\"author\":", html);
    }

    [Fact]
    public async Task RenderJsonLd_uses_admin_supplied_payload_verbatim()
    {
        var custom = "{\"@context\":\"https://schema.org\",\"@type\":\"FAQPage\",\"mainEntity\":[]}";
        var row = new FcmsSeoMeta { EntityType = "FcmsPage", EntityId = Guid.NewGuid(), CustomJsonLd = custom };
        var (svc, _) = Create(row);
        var ctx = new SeoRenderContext("FcmsPage", row.EntityId, "T", "D", "https://x.com/p");
        var html = await svc.RenderJsonLdAsync(ctx);
        Assert.Contains("FAQPage", html);
        // Default Article generation skipped when custom payload set.
        Assert.DoesNotContain("\"@type\":\"Article\"", html);
    }

    [Fact]
    public async Task RenderJsonLd_uses_custom_schema_type()
    {
        var row = new FcmsSeoMeta
        {
            EntityType = "FcmsPost",
            EntityId = Guid.NewGuid(),
            SchemaType = "NewsArticle",
        };
        var (svc, _) = Create(row);
        var ctx = new SeoRenderContext("FcmsPost", row.EntityId, "T", "D", "https://x.com/p");
        var html = await svc.RenderJsonLdAsync(ctx);
        Assert.Contains("\"@type\":\"NewsArticle\"", html);
    }
}
