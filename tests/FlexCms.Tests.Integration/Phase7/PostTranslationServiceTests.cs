using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace FlexCms.Tests.Integration.Phase7;

public class PostTranslationServiceTests : IDisposable
{
    private readonly FcmsDbContext _db;
    private readonly PostService _svc;

    public PostTranslationServiceTests()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FcmsDbContext(opts);
#pragma warning disable CA2000
        _svc = new PostService(
            new EfRepository<FcmsPost>(_db),
            new EfRepository<FcmsTag>(_db),
            new EfRepository<FcmsPostTag>(_db),
            new EfRepository<FcmsPostTranslation>(_db),
            new EfUnitOfWork(_db));
#pragma warning restore CA2000
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task SaveTranslationAsync_inserts_then_updates()
    {
        var post = await _svc.CreateAsync(new FcmsPost { Title = "T", Slug = "t", Content = "" }, []);

        var first = await _svc.SaveTranslationAsync(new FcmsPostTranslation
        {
            PostId = post.Id,
            LanguageCode = "bn",
            Title = "BN",
            Slug = "t-bn",
            Content = "v1"
        });
        var second = await _svc.SaveTranslationAsync(new FcmsPostTranslation
        {
            PostId = post.Id,
            LanguageCode = "bn",
            Title = "BN2",
            Slug = "t-bn",
            Content = "v2"
        });

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await _db.PostTranslations.CountAsync());
        Assert.Equal("BN2", second.Title);
    }

    [Fact]
    public async Task ResolveBySlugAsync_translation_slug_match_wins()
    {
        var post = await _svc.CreateAsync(new FcmsPost { Title = "T", Slug = "t-en", Content = "EN" }, []);
        await _svc.SaveTranslationAsync(new FcmsPostTranslation
        {
            PostId = post.Id,
            LanguageCode = "bn",
            Title = "BN",
            Slug = "t-bn",
            Excerpt = "ex",
            Content = "BN"
        });

        var r = await _svc.ResolveBySlugAsync("t-bn", "bn");

        Assert.NotNull(r);
        Assert.Equal(post.Id, r!.Value.Post.Id);
        Assert.Equal("BN", r.Value.Translation!.Title);
        Assert.Equal("ex", r.Value.Translation.Excerpt);
    }

    [Fact]
    public async Task ResolveBySlugAsync_base_slug_returns_overlay_when_translation_exists()
    {
        var post = await _svc.CreateAsync(new FcmsPost { Title = "T", Slug = "t-en", Content = "EN" }, []);
        await _svc.SaveTranslationAsync(new FcmsPostTranslation
        {
            PostId = post.Id,
            LanguageCode = "bn",
            Title = "BN",
            Slug = "t-bn",
            Content = "BN"
        });

        var r = await _svc.ResolveBySlugAsync("t-en", "bn");

        Assert.NotNull(r);
        Assert.NotNull(r!.Value.Translation);
        Assert.Equal("BN", r.Value.Translation!.Title);
    }

    [Fact]
    public async Task ResolveBySlugAsync_no_translation_returns_base()
    {
        await _svc.CreateAsync(new FcmsPost { Title = "T", Slug = "t-en", Content = "EN" }, []);
        var r = await _svc.ResolveBySlugAsync("t-en", "bn");
        Assert.NotNull(r);
        Assert.Null(r!.Value.Translation);
    }
}
