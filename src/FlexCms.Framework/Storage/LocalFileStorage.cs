using Microsoft.AspNetCore.Hosting;

namespace FlexCms.Framework.Storage;

public class LocalFileStorage : IFcmsFileStorage
{
    private readonly string _webRootPath;
    private readonly string _baseUrl;

    public LocalFileStorage(IWebHostEnvironment env)
    {
        _webRootPath = env.WebRootPath;
        _baseUrl = "";
    }

    public async Task<string> SaveAsync(string relativePath, Stream content, CancellationToken ct = default)
    {
        var fullPath = ResolveSafe(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(fs, ct);

        return $"{_baseUrl}/{relativePath.TrimStart('/')}";
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        var fullPath = ResolveSafe(relativePath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default)
    {
        var fullPath = ResolveSafe(relativePath);
        return Task.FromResult(File.Exists(fullPath));
    }

    private string ResolveSafe(string relativePath)
    {
        var normalized = relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_webRootPath, normalized));

        if (!fullPath.StartsWith(_webRootPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Path traversal detected.");

        return fullPath;
    }
}
