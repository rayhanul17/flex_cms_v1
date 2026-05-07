using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace FlexCms.Tests.Integration.Phase7;

/// <summary>
/// Phase 7 — content translation flow against EF in-memory:
/// add/get/list/delete translations, language-aware slug resolution, and
/// fallback to the base page when the requested language has no translation.
/// </summary>
public class PageTranslationServiceTests : IDisposable
{
    private readonly FcmsDbContext _db;
    private readonly PageService _svc;

    public PageTranslationServiceTests()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FcmsDbContext(opts);
#pragma warning disable CA2000
        _svc = new PageService(
            new EfRepository<FcmsPage>(_db),
            new EfRepository<FcmsPageTranslation>(_db),
            new EfUnitOfWork(_db));
#pragma warning restore CA2000
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task SaveTranslationAsync_inserts_when_no_existing_row()
    {
        var page = await _svc.CreateAsync(new FcmsPage { Title = "About", Slug = "about", Content = "" });

        var tr = await _svc.SaveTranslationAsync(new FcmsPageTranslation
        {
            PageId = page.Id,
            LanguageCode = "bn",
            Title = "আমাদের সম্পর্কে",
            Slug = "about-bn",
            Content = "<p>বিষয়বস্তু</p>"
        });

        Assert.NotEqual(Guid.Empty, tr.Id);
        Assert.Equal(1, await _db.PageTranslations.CountAsync());
    }

    [Fact]
    public async Task SaveTranslationAsync_updates_existing_row_in_place()
    {
        var page = await _svc.CreateAsync(new FcmsPage { Title = "About", Slug = "about", Content = "" });

        var first = await _svc.SaveTranslationAsync(new FcmsPageTranslation
        {
            PageId = page.Id,
            LanguageCode = "bn",
            Title = "প্রথম",
            Slug = "about-bn",
            Content = ""
        });

        var second = await _svc.SaveTranslationAsync(new FcmsPageTranslation
        {
            PageId = page.Id,
            LanguageCode = "bn",
            Title = "দ্বিতীয়",
            Slug = "about-bn",
            Content = ""
        });

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await _db.PageTranslations.CountAsync());
        Assert.Equal("দ্বিতীয়", second.Title);
    }

    [Fact]
    public async Task SaveTranslationAsync_normalizes_language_code_to_lowercase()
    {
        var page = await _svc.CreateAsync(new FcmsPage { Title = "About", Slug = "about", Content = "" });
        var tr = await _svc.SaveTranslationAsync(new FcmsPageTranslation
        {
            PageId = page.Id,
            LanguageCode = "BN",
            Title = "x",
            Slug = "about-bn",
            Content = ""
        });
        Assert.Equal("bn", tr.LanguageCode);
    }

    [Fact]
    public async Task ResolveBySlugAsync_translation_slug_match_returns_base_and_translation()
    {
        var page = await _svc.CreateAsync(new FcmsPage { Title = "About", Slug = "about", Content = "EN" });
        await _svc.SaveTranslationAsync(new FcmsPageTranslation
        {
            PageId = page.Id,
            LanguageCode = "bn",
            Title = "BN-Title",
            Slug = "about-bn",
            Content = "BN"
        });

        var resolved = await _svc.ResolveBySlugAsync("about-bn", "bn");

        Assert.NotNull(resolved);
        Assert.Equal(page.Id, resolved!.Value.Page.Id);
        Assert.NotNull(resolved.Value.Translation);
        Assert.Equal("BN-Title", resolved.Value.Translation!.Title);
    }

    [Fact]
    public async Task ResolveBySlugAsync_base_slug_in_other_lang_returns_translation_overlay()
    {
        var page = await _svc.CreateAsync(new FcmsPage { Title = "About", Slug = "about", Content = "EN" });
        await _svc.SaveTranslationAsync(new FcmsPageTranslation
        {
            PageId = page.Id,
            LanguageCode = "bn",
            Title = "BN-Title",
            Slug = "about-bn",
            Content = "BN"
        });

        // Hitting /about while in bn language → base slug match wins; translation overlay served.
        var resolved = await _svc.ResolveBySlugAsync("about", "bn");

        Assert.NotNull(resolved);
        Assert.Equal(page.Id, resolved!.Value.Page.Id);
        Assert.NotNull(resolved.Value.Translation);
        Assert.Equal("BN-Title", resolved.Value.Translation!.Title);
    }

    [Fact]
    public async Task ResolveBySlugAsync_falls_back_to_base_when_no_translation_for_lang()
    {
        await _svc.CreateAsync(new FcmsPage { Title = "About", Slug = "about", Content = "EN" });

        // No bn translation row exists at all → base content served, no 404.
        var resolved = await _svc.ResolveBySlugAsync("about", "bn");

        Assert.NotNull(resolved);
        Assert.Null(resolved!.Value.Translation);
        Assert.Equal("About", resolved.Value.Page.Title);
    }

    [Fact]
    public async Task ResolveBySlugAsync_returns_null_for_unknown_slug()
    {
        var resolved = await _svc.ResolveBySlugAsync("does-not-exist", "en");
        Assert.Null(resolved);
    }

    [Fact]
    public async Task GetTranslationsAsync_returns_all_languages_for_page()
    {
        var page = await _svc.CreateAsync(new FcmsPage { Title = "About", Slug = "about", Content = "" });
        await _svc.SaveTranslationAsync(new FcmsPageTranslation { PageId = page.Id, LanguageCode = "bn", Title = "B", Slug = "ab-bn", Content = "" });
        await _svc.SaveTranslationAsync(new FcmsPageTranslation { PageId = page.Id, LanguageCode = "fr", Title = "F", Slug = "ab-fr", Content = "" });

        var list = await _svc.GetTranslationsAsync(page.Id);

        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task DeleteTranslationAsync_removes_row()
    {
        var page = await _svc.CreateAsync(new FcmsPage { Title = "About", Slug = "about", Content = "" });
        var tr = await _svc.SaveTranslationAsync(new FcmsPageTranslation
        {
            PageId = page.Id,
            LanguageCode = "bn",
            Title = "x",
            Slug = "about-bn",
            Content = ""
        });

        await _svc.DeleteTranslationAsync(tr.Id);

        Assert.Equal(0, await _db.PageTranslations.CountAsync());
    }
}
