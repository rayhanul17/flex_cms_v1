using FlexCms.Framework.Cms;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Services;
using FlexCms.Framework.Storage;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace FlexCms.Tests.Integration.Hardening;

/// <summary>
/// Verify bulk alt-text update semantics: only changed rows persist,
/// missing ids skipped, whitespace-only normalized to null. Pairs with
/// the admin /admin/media/alt-text page that POSTs the dirty rows.
/// </summary>
public class BulkAltTextTests : IDisposable
{
    private readonly FcmsDbContext _db;
    private readonly MediaService _svc;

    public BulkAltTextTests()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FcmsDbContext(opts);
#pragma warning disable CA2000
        _svc = new MediaService(
            new EfRepository<FcmsMedia>(_db),
            new EfUnitOfWork(_db),
            Substitute.For<IFcmsFileStorage>(),
            Substitute.For<IFcmsLogService>(),
            Substitute.For<ISettingsService>());
#pragma warning restore CA2000
    }

    public void Dispose() => _db.Dispose();

    private async Task<FcmsMedia> SeedAsync(string? altText = null)
    {
        var m = new FcmsMedia
        {
            Id = Guid.NewGuid(),
            FileName = "x.jpg",
            OriginalFileName = "x.jpg",
            MimeType = "image/jpeg",
            Extension = ".jpg",
            FileSize = 100,
            Url = "/uploads/x.jpg",
            AltText = altText,
        };
        _db.Media.Add(m);
        await _db.SaveChangesAsync();
        return m;
    }

    [Fact]
    public async Task Updates_alt_text_when_changed()
    {
        var a = await SeedAsync();
        var b = await SeedAsync();

        var n = await _svc.BulkUpdateAltTextAsync(new Dictionary<Guid, string?>
        {
            [a.Id] = "Photo of Alice",
            [b.Id] = "Photo of Bob",
        });
        Assert.Equal(2, n);

        Assert.Equal("Photo of Alice", (await _db.Media.FirstAsync(m => m.Id == a.Id)).AltText);
        Assert.Equal("Photo of Bob", (await _db.Media.FirstAsync(m => m.Id == b.Id)).AltText);
    }

    [Fact]
    public async Task Skips_rows_where_value_unchanged()
    {
        var a = await SeedAsync(altText: "Existing");
        var b = await SeedAsync(altText: "Existing");

        // Only b's entry differs — bulk should report 1.
        var n = await _svc.BulkUpdateAltTextAsync(new Dictionary<Guid, string?>
        {
            [a.Id] = "Existing",   // unchanged
            [b.Id] = "Different",  // changed
        });
        Assert.Equal(1, n);
    }

    [Fact]
    public async Task Whitespace_only_input_normalized_to_null()
    {
        var a = await SeedAsync(altText: "Old");

        var n = await _svc.BulkUpdateAltTextAsync(new Dictionary<Guid, string?>
        {
            [a.Id] = "   ",
        });
        Assert.Equal(1, n);
        Assert.Null((await _db.Media.FirstAsync(m => m.Id == a.Id)).AltText);
    }

    [Fact]
    public async Task Trims_leading_and_trailing_whitespace()
    {
        var a = await SeedAsync();
        var n = await _svc.BulkUpdateAltTextAsync(new Dictionary<Guid, string?>
        {
            [a.Id] = "  Cropped photo  ",
        });
        Assert.Equal(1, n);
        Assert.Equal("Cropped photo", (await _db.Media.FirstAsync(m => m.Id == a.Id)).AltText);
    }

    [Fact]
    public async Task Skips_unknown_ids_silently()
    {
        // Stale form: admin opened the page, someone deleted a media item,
        // admin saved. The bulk update should silently skip the dead id +
        // succeed for the live ones.
        var a = await SeedAsync();
        var n = await _svc.BulkUpdateAltTextAsync(new Dictionary<Guid, string?>
        {
            [a.Id] = "ok",
            [Guid.NewGuid()] = "this id was deleted",
        });
        Assert.Equal(1, n);
    }

    [Fact]
    public async Task Empty_input_returns_zero()
    {
        Assert.Equal(0, await _svc.BulkUpdateAltTextAsync(new Dictionary<Guid, string?>()));
    }
}
