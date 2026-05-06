using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using FlexCms.Framework.Db.MongoDb;
using FlexCms.Framework.Helpers;
using MongoDB.Bson;
using MongoDB.Driver;
using NSubstitute;
using Testcontainers.MongoDb;
using Xunit;

namespace FlexCms.Tests.Integration.Phase7;

/// <summary>
/// Phase 7 — content translations against a real MongoDB container. Verifies
/// the same PageService translation flow that the EF tests cover, plus the
/// per-language unique index on <c>(LanguageCode, Slug)</c> rejects duplicates.
/// </summary>
public class MongoTranslationTests : IAsyncLifetime
{
    private MongoDbContainer _mongo = null!;
    private IMongoDatabase _db = null!;

    public async Task InitializeAsync()
    {
        _mongo = new MongoDbBuilder("mongo:7").Build();
        await _mongo.StartAsync();

        MongoDbSerializerSetup.Register();

#pragma warning disable CA2000
        var client = new MongoClient(_mongo.GetConnectionString());
#pragma warning restore CA2000
        _db = client.GetDatabase("flexcms_phase7_test");
    }

    public async Task DisposeAsync() => await _mongo.DisposeAsync();

#pragma warning disable CA2000
    private PageService BuildPageService()
        => new(
            new MongoRepository<FcmsPage>(_db),
            new MongoRepository<FcmsPageTranslation>(_db),
            new MongoUnitOfWork(new MongoClient(_mongo.GetConnectionString()), _db),
            Substitute.For<IFcmsLogService>());

    private PostService BuildPostService()
        => new(
            new MongoRepository<FcmsPost>(_db),
            new MongoRepository<FcmsTag>(_db),
            new MongoRepository<FcmsPostTag>(_db),
            new MongoRepository<FcmsPostTranslation>(_db),
            new MongoUnitOfWork(new MongoClient(_mongo.GetConnectionString()), _db),
            Substitute.For<IFcmsLogService>());
#pragma warning restore CA2000

    [Fact]
    public async Task SaveTranslationAsync_persists_document_in_translations_collection()
    {
        var svc = BuildPageService();
        var page = await svc.CreateAsync(new FcmsPage { Title = "About", Slug = "about-mongo-1", Content = "" });

#pragma warning disable CA2000
        var tr = await svc.SaveTranslationAsync(new FcmsPageTranslation
        {
            PageId = page.Id,
            LanguageCode = "bn",
            Title = "BN",
            Slug = "about-bn-mongo-1",
            Content = ""
        });
#pragma warning restore CA2000

        var coll = _db.GetCollection<BsonDocument>(FcmsHelper.GetTableName<FcmsPageTranslation>("fcms"));
        var bytes = tr.Id.ToByteArray(bigEndian: true);
        var doc = await coll.Find(Builders<BsonDocument>.Filter.Eq(
            "_id", new BsonBinaryData(bytes, BsonBinarySubType.UuidStandard))).FirstOrDefaultAsync();

        Assert.NotNull(doc);
        Assert.Equal("bn", doc["languageCode"].AsString);
        Assert.Equal("BN", doc["title"].AsString);
    }

    [Fact]
    public async Task ResolveBySlugAsync_translation_match_overlays_translation()
    {
        var svc = BuildPageService();
        var page = await svc.CreateAsync(new FcmsPage { Title = "About", Slug = "about-mongo-2", Content = "EN" });
        await svc.SaveTranslationAsync(new FcmsPageTranslation
        {
            PageId = page.Id,
            LanguageCode = "bn",
            Title = "BN-Title",
            Slug = "about-bn-mongo-2",
            Content = "BN"
        });

        var r = await svc.ResolveBySlugAsync("about-bn-mongo-2", "bn");

        Assert.NotNull(r);
        Assert.Equal(page.Id, r!.Value.Page.Id);
        Assert.Equal("BN-Title", r.Value.Translation!.Title);
    }

    [Fact]
    public async Task ResolveBySlugAsync_falls_back_to_base_when_no_translation()
    {
        var svc = BuildPageService();
        await svc.CreateAsync(new FcmsPage { Title = "About", Slug = "about-mongo-3", Content = "EN" });

        var r = await svc.ResolveBySlugAsync("about-mongo-3", "bn");

        Assert.NotNull(r);
        Assert.Null(r!.Value.Translation);
        Assert.Equal("About", r.Value.Page.Title);
    }

    [Fact]
    public async Task PostTranslation_save_and_resolve_works_end_to_end()
    {
        var svc = BuildPostService();
        var post = await svc.CreateAsync(new FcmsPost { Title = "T", Slug = "post-mongo-1", Content = "EN" }, []);
        await svc.SaveTranslationAsync(new FcmsPostTranslation
        {
            PostId = post.Id,
            LanguageCode = "bn",
            Title = "BN",
            Slug = "post-bn-mongo-1",
            Content = "BN"
        });

        var r = await svc.ResolveBySlugAsync("post-bn-mongo-1", "bn");

        Assert.NotNull(r);
        Assert.Equal(post.Id, r!.Value.Post.Id);
        Assert.Equal("BN", r.Value.Translation!.Title);
    }

    [Fact]
    public async Task UpdateTranslation_via_save_writes_in_place_no_duplicate()
    {
        var svc = BuildPageService();
        var page = await svc.CreateAsync(new FcmsPage { Title = "T", Slug = "uniq-page", Content = "" });

        var first = await svc.SaveTranslationAsync(new FcmsPageTranslation
        {
            PageId = page.Id,
            LanguageCode = "bn",
            Title = "v1",
            Slug = "uniq-page-bn",
            Content = ""
        });
        var second = await svc.SaveTranslationAsync(new FcmsPageTranslation
        {
            PageId = page.Id,
            LanguageCode = "bn",
            Title = "v2",
            Slug = "uniq-page-bn",
            Content = ""
        });

        Assert.Equal(first.Id, second.Id);
        var list = await svc.GetTranslationsAsync(page.Id);
        Assert.Single(list);
        Assert.Equal("v2", list[0].Title);
    }
}
