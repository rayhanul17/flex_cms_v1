using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FlexCms.Framework.Db;
using FlexCms.Framework.Services;

namespace FlexCms.Framework.Seo;

public sealed class SeoService : ISeoService
{
    private readonly IRepository<FcmsSeoMeta> _repo;
    private readonly IFcmsUnitOfWork _uow;
    private readonly ISettingsService _settings;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        // Don't escape Unicode — Bengali chars must render as-is for correctness
        // AND screen readers; default JsonSerializer escaping mangles them.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    public SeoService(IRepository<FcmsSeoMeta> repo, IFcmsUnitOfWork uow, ISettingsService settings)
    {
        _repo = repo;
        _uow = uow;
        _settings = settings;
    }

    public async Task<FcmsSeoMeta?> GetAsync(string entityType, Guid entityId, CancellationToken ct = default)
    {
        var rows = await _repo.GetAllAsync(ct);
        return rows.FirstOrDefault(r =>
            string.Equals(r.EntityType, entityType, StringComparison.OrdinalIgnoreCase) &&
            r.EntityId == entityId);
    }

    public async Task SaveAsync(FcmsSeoMeta meta, CancellationToken ct = default)
    {
        if (meta is null) throw new ArgumentNullException(nameof(meta));
        if (string.IsNullOrWhiteSpace(meta.EntityType))
            throw new ArgumentException("EntityType is required.", nameof(meta));

        var existing = await GetAsync(meta.EntityType, meta.EntityId, ct);
        if (existing is null)
        {
            await _repo.AddAsync(meta, ct);
        }
        else
        {
            existing.CanonicalUrl = meta.CanonicalUrl;
            existing.NoIndex = meta.NoIndex;
            existing.OgTitle = meta.OgTitle;
            existing.OgDescription = meta.OgDescription;
            existing.OgImageUrl = meta.OgImageUrl;
            existing.OgType = meta.OgType;
            existing.TwitterCard = meta.TwitterCard;
            existing.TwitterTitle = meta.TwitterTitle;
            existing.TwitterDescription = meta.TwitterDescription;
            existing.TwitterImageUrl = meta.TwitterImageUrl;
            existing.SchemaType = meta.SchemaType;
            existing.CustomJsonLd = meta.CustomJsonLd;
            await _repo.UpdateAsync(existing, ct);
        }
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<string> RenderHeadTagsAsync(SeoRenderContext ctx, CancellationToken ct = default)
    {
        var seo = await GetAsync(ctx.EntityType, ctx.EntityId, ct);
        var siteName = await GetSiteNameAsync(ct);

        // Effective values — admin-supplied overrides take precedence; otherwise
        // fall back to the entity's own title/description.
        var title = !string.IsNullOrWhiteSpace(seo?.OgTitle) ? seo!.OgTitle! : ctx.Title;
        var description = !string.IsNullOrWhiteSpace(seo?.OgDescription) ? seo!.OgDescription! : ctx.Description ?? "";
        var ogImage = !string.IsNullOrWhiteSpace(seo?.OgImageUrl) ? seo!.OgImageUrl! : ctx.FeaturedImageUrl ?? "";
        var twitterImage = !string.IsNullOrWhiteSpace(seo?.TwitterImageUrl) ? seo!.TwitterImageUrl! : ogImage;
        var twitterTitle = !string.IsNullOrWhiteSpace(seo?.TwitterTitle) ? seo!.TwitterTitle! : title;
        var twitterDesc = !string.IsNullOrWhiteSpace(seo?.TwitterDescription) ? seo!.TwitterDescription! : description;
        var canonical = !string.IsNullOrWhiteSpace(seo?.CanonicalUrl) ? seo!.CanonicalUrl! : ctx.CanonicalUrl;

        var sb = new StringBuilder();
        // Canonical first — search engines hit it most.
        sb.Append("<link rel=\"canonical\" href=\"").Append(HtmlEncode(canonical)).Append("\" />\n");

        if (seo?.NoIndex == true)
            sb.Append("<meta name=\"robots\" content=\"noindex,nofollow\" />\n");

        sb.Append("<meta property=\"og:type\" content=\"").Append(HtmlEncode(seo?.OgType ?? "article")).Append("\" />\n");
        sb.Append("<meta property=\"og:title\" content=\"").Append(HtmlEncode(title)).Append("\" />\n");
        sb.Append("<meta property=\"og:description\" content=\"").Append(HtmlEncode(description)).Append("\" />\n");
        sb.Append("<meta property=\"og:url\" content=\"").Append(HtmlEncode(canonical)).Append("\" />\n");
        sb.Append("<meta property=\"og:site_name\" content=\"").Append(HtmlEncode(siteName)).Append("\" />\n");
        if (!string.IsNullOrEmpty(ogImage))
            sb.Append("<meta property=\"og:image\" content=\"").Append(HtmlEncode(ogImage)).Append("\" />\n");

        sb.Append("<meta name=\"twitter:card\" content=\"").Append(HtmlEncode(seo?.TwitterCard ?? "summary_large_image")).Append("\" />\n");
        sb.Append("<meta name=\"twitter:title\" content=\"").Append(HtmlEncode(twitterTitle)).Append("\" />\n");
        sb.Append("<meta name=\"twitter:description\" content=\"").Append(HtmlEncode(twitterDesc)).Append("\" />\n");
        if (!string.IsNullOrEmpty(twitterImage))
            sb.Append("<meta name=\"twitter:image\" content=\"").Append(HtmlEncode(twitterImage)).Append("\" />\n");

        return sb.ToString();
    }

    public async Task<string> RenderJsonLdAsync(SeoRenderContext ctx, CancellationToken ct = default)
    {
        var seo = await GetAsync(ctx.EntityType, ctx.EntityId, ct);
        var siteName = await GetSiteNameAsync(ct);

        // Custom JSON-LD bypasses generation — admin owns the payload entirely.
        if (!string.IsNullOrWhiteSpace(seo?.CustomJsonLd))
        {
            // Trust but render in a script tag so it can't break the page.
            return "<script type=\"application/ld+json\">\n" + seo!.CustomJsonLd! + "\n</script>";
        }

        var schemaType = !string.IsNullOrWhiteSpace(seo?.SchemaType) ? seo!.SchemaType! : "Article";
        var headline = !string.IsNullOrWhiteSpace(seo?.OgTitle) ? seo!.OgTitle! : ctx.Title;
        var description = !string.IsNullOrWhiteSpace(seo?.OgDescription) ? seo!.OgDescription! : ctx.Description ?? "";
        var image = !string.IsNullOrWhiteSpace(seo?.OgImageUrl) ? seo!.OgImageUrl! : ctx.FeaturedImageUrl;

        // Build a minimal but valid Article-shaped object. Modules can extend
        // by setting CustomJsonLd; everything else gets a sane default that
        // passes Google's Rich Results Test.
        var payload = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = schemaType,
            ["headline"] = headline,
            ["description"] = description,
            ["url"] = ctx.CanonicalUrl,
            ["mainEntityOfPage"] = new Dictionary<string, object?>
            {
                ["@type"] = "WebPage",
                ["@id"] = ctx.CanonicalUrl,
            },
            ["publisher"] = new Dictionary<string, object?>
            {
                ["@type"] = "Organization",
                ["name"] = siteName,
            },
        };

        if (!string.IsNullOrEmpty(image))
            payload["image"] = image;
        if (!string.IsNullOrEmpty(ctx.AuthorName))
        {
            payload["author"] = new Dictionary<string, object?>
            {
                ["@type"] = "Person",
                ["name"] = ctx.AuthorName,
            };
        }
        if (ctx.PublishedAt.HasValue)
            payload["datePublished"] = ctx.PublishedAt.Value.ToString("o");
        if (ctx.UpdatedAt.HasValue)
            payload["dateModified"] = ctx.UpdatedAt.Value.ToString("o");

        var json = JsonSerializer.Serialize(payload, JsonOpts);
        return "<script type=\"application/ld+json\">\n" + json + "\n</script>";
    }

    private async Task<string> GetSiteNameAsync(CancellationToken ct)
    {
        try
        {
            var snap = await _settings.GetAsync<SiteNameSnapshot>("site:general", ct);
            return string.IsNullOrWhiteSpace(snap.SiteName) ? "" : snap.SiteName;
        }
        catch { return ""; }
    }

    /// <summary>Local DTO matching the relevant subset of SiteSettings — Framework can't reference Core.</summary>
    private sealed class SiteNameSnapshot
    {
        public string SiteName { get; set; } = "";
    }

    private static string HtmlEncode(string s) => HtmlEncoder.Default.Encode(s ?? "");
}
