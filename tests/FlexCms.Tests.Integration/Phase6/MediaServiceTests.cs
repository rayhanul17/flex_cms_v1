using FlexCms.Framework.Cms;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace FlexCms.Tests.Integration.Phase6;

/// <summary>
/// Integration tests for MediaService using EF InMemory + stubbed IFcmsFileStorage.
/// Covers: upload validation, soft-delete, folder move, GetByFolder.
/// </summary>
public class MediaServiceTests : IDisposable
{
    private readonly FcmsDbContext _db;
    private readonly IFcmsFileStorage _storage;
    private readonly MediaService _svc;

    public MediaServiceTests()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FcmsDbContext(opts);
        _storage = Substitute.For<IFcmsFileStorage>();

        // Default: storage saves succeed and return a predictable URL
        _storage.SaveAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(ci => "/" + ci.ArgAt<string>(0));
        _storage.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var mediaRepo = new EfRepository<FcmsMedia>(_db);
        _svc = new MediaService(mediaRepo, _storage);
    }

    public void Dispose() => _db.Dispose();

    // ── Upload validation ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(".exe")]
    [InlineData(".php")]
    [InlineData(".bat")]
    [InlineData(".sh")]
    public async Task Upload_rejects_disallowed_extension(string ext)
    {
        var file = MakeFormFile($"malware{ext}", ext, "application/octet-stream");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.UploadAsync(file, null));
    }

    [Fact]
    public async Task Upload_rejects_svg_file()
    {
        // SVG was intentionally removed due to XSS risk
        var file = MakeFormFile("image.svg", ".svg", "image/svg+xml", "<svg/>"u8.ToArray());
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.UploadAsync(file, null));
    }

    [Fact]
    public async Task Upload_rejects_file_with_wrong_magic_bytes()
    {
        // Claim to be a PNG but send JPEG bytes
        var jpegHeader = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 };
        var file = MakeFormFile("fake.png", ".png", "image/png", jpegHeader);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.UploadAsync(file, null));
    }

    [Fact]
    public async Task Upload_pdf_with_valid_magic_bytes_succeeds()
    {
        var pdfHeader = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E }; // %PDF-1.
        var file = MakeFormFile("doc.pdf", ".pdf", "application/pdf", pdfHeader);

        var media = await _svc.UploadAsync(file, null);

        Assert.NotEqual(Guid.Empty, media.Id);
        Assert.Equal(".pdf", media.Extension);
        Assert.Null(media.ThumbnailUrl); // no thumbnail for non-images
    }

    [Fact]
    public async Task Upload_stores_media_in_db()
    {
        var pdfHeader = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        var file = MakeFormFile("report.pdf", ".pdf", "application/pdf", pdfHeader);

        var media = await _svc.UploadAsync(file, null);

        var stored = await _db.Set<FcmsMedia>().FindAsync(media.Id);
        Assert.NotNull(stored);
        Assert.Equal("report.pdf", stored.OriginalFileName);
    }

    [Fact]
    public async Task Upload_assigns_folder_id()
    {
        var folderId = Guid.NewGuid();
        var pdfHeader = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        var file = MakeFormFile("file.pdf", ".pdf", "application/pdf", pdfHeader);

        var media = await _svc.UploadAsync(file, folderId);

        Assert.Equal(folderId, media.FolderId);
    }

    // ── SoftDelete ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SoftDelete_marks_entity_deleted_and_calls_storage_delete()
    {
        var media = await InsertMediaAsync();

        await _svc.SoftDeleteAsync(media.Id);

        var raw = await _db.Set<FcmsMedia>().IgnoreQueryFilters().FirstAsync(m => m.Id == media.Id);
        Assert.True(raw.IsDeleted);
        await _storage.Received().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SoftDelete_nonexistent_throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SoftDeleteAsync(Guid.NewGuid()));
    }

    // ── GetByFolder ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByFolder_returns_only_matching_folder()
    {
        var folderId = Guid.NewGuid();
        await InsertMediaAsync(folderId: folderId);
        await InsertMediaAsync(folderId: folderId);
        await InsertMediaAsync(folderId: Guid.NewGuid()); // different folder

        var result = await _svc.GetByFolderAsync(folderId);

        Assert.Equal(2, result.Count);
        Assert.All(result, m => Assert.Equal(folderId, m.FolderId));
    }

    [Fact]
    public async Task GetByFolder_null_returns_root_items()
    {
        await InsertMediaAsync(folderId: null);
        await InsertMediaAsync(folderId: null);
        await InsertMediaAsync(folderId: Guid.NewGuid());

        var result = await _svc.GetByFolderAsync(null);

        Assert.Equal(2, result.Count);
        Assert.All(result, m => Assert.Null(m.FolderId));
    }

    // ── MoveToFolder ──────────────────────────────────────────────────────────

    [Fact]
    public async Task MoveToFolder_updates_folder_id()
    {
        var media = await InsertMediaAsync(folderId: null);
        var targetFolder = Guid.NewGuid();

        await _svc.MoveToFolderAsync(media.Id, targetFolder);

        var updated = await _db.Set<FcmsMedia>().FindAsync(media.Id);
        Assert.Equal(targetFolder, updated!.FolderId);
    }

    [Fact]
    public async Task MoveToFolder_nonexistent_throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _svc.MoveToFolderAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    // ── GetById ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_returns_entity()
    {
        var media = await InsertMediaAsync();
        var result = await _svc.GetByIdAsync(media.Id);
        Assert.NotNull(result);
        Assert.Equal(media.Id, result.Id);
    }

    [Fact]
    public async Task GetById_soft_deleted_returns_null()
    {
        var media = await InsertMediaAsync();
        await _svc.SoftDeleteAsync(media.Id);
        await _db.SaveChangesAsync();

        var result = await _svc.GetByIdAsync(media.Id);
        Assert.Null(result);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<FcmsMedia> InsertMediaAsync(Guid? folderId = null)
    {
        var media = new FcmsMedia
        {
            FileName = $"file_{Guid.NewGuid():N}.pdf",
            OriginalFileName = "original.pdf",
            MimeType = "application/pdf",
            Extension = ".pdf",
            FileSize = 1024,
            Url = "/uploads/media/file.pdf",
            FolderId = folderId
        };
        _db.Set<FcmsMedia>().Add(media);
        await _db.SaveChangesAsync();
        return media;
    }

    private static IFormFile MakeFormFile(string fileName, string ext, string contentType, byte[]? content = null)
    {
        content ??= Array.Empty<byte>();
        var ms = new MemoryStream(content);
        var file = Substitute.For<IFormFile>();
        file.FileName.Returns(fileName);
        file.ContentType.Returns(contentType);
        file.Length.Returns(content.LongLength);
        file.CopyToAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(ci => ms.CopyToAsync(ci.ArgAt<Stream>(0), ci.ArgAt<CancellationToken>(1)));
        return file;
    }
}
