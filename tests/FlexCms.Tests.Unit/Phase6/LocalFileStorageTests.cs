using FlexCms.Framework.Storage;
using Microsoft.AspNetCore.Hosting;
using NSubstitute;

namespace FlexCms.Tests.Unit.Phase6;

/// <summary>
/// Unit tests for LocalFileStorage — path traversal guard, save/delete/exists.
/// Uses a real temp directory; no mocking of the file system needed.
/// </summary>
public class LocalFileStorageTests : IDisposable
{
    private readonly string _webRoot;
    private readonly LocalFileStorage _storage;

    public LocalFileStorageTests()
    {
        _webRoot = Path.Combine(Path.GetTempPath(), "fcms_storage_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_webRoot);

        var env = Substitute.For<IWebHostEnvironment>();
        env.WebRootPath.Returns(_webRoot);
        _storage = new LocalFileStorage(env);
    }

    public void Dispose()
    {
        if (Directory.Exists(_webRoot))
            Directory.Delete(_webRoot, recursive: true);
    }

    [Fact]
    public async Task SaveAsync_creates_file_and_returns_url()
    {
        using var ms = new MemoryStream("hello"u8.ToArray());
        var url = await _storage.SaveAsync("uploads/media/test.txt", ms);

        Assert.True(File.Exists(Path.Combine(_webRoot, "uploads", "media", "test.txt")));
        Assert.Contains("uploads/media/test.txt", url);
    }

    [Fact]
    public async Task ExistsAsync_returns_true_after_save()
    {
        using var ms = new MemoryStream("data"u8.ToArray());
        await _storage.SaveAsync("uploads/exist.txt", ms);

        Assert.True(await _storage.ExistsAsync("uploads/exist.txt"));
    }

    [Fact]
    public async Task ExistsAsync_returns_false_for_missing_file()
    {
        Assert.False(await _storage.ExistsAsync("uploads/not_there.txt"));
    }

    [Fact]
    public async Task DeleteAsync_removes_file()
    {
        using var ms = new MemoryStream("bye"u8.ToArray());
        await _storage.SaveAsync("uploads/todelete.txt", ms);

        await _storage.DeleteAsync("uploads/todelete.txt");

        Assert.False(File.Exists(Path.Combine(_webRoot, "uploads", "todelete.txt")));
    }

    [Fact]
    public async Task DeleteAsync_nonexistent_file_does_not_throw()
    {
        // Should be a no-op
        await _storage.DeleteAsync("uploads/ghost.txt");
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("../../etc/passwd")]
    [InlineData("uploads/../../outside.txt")]
    public async Task SaveAsync_path_traversal_throws(string path)
    {
        using var ms = new MemoryStream("bad"u8.ToArray());
        await Assert.ThrowsAsync<InvalidOperationException>(() => _storage.SaveAsync(path, ms));
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("../../etc/passwd")]
    public async Task DeleteAsync_path_traversal_throws(string path)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _storage.DeleteAsync(path));
    }

    [Theory]
    [InlineData("../secret.txt")]
    public async Task ExistsAsync_path_traversal_throws(string path)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _storage.ExistsAsync(path));
    }
}
