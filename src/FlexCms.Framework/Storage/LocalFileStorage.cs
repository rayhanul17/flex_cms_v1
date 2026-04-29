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
        var fullPath = Path.Combine(_webRootPath, relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(fs, ct);

        return $"{_baseUrl}/{relativePath.TrimStart('/')}";
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_webRootPath, relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_webRootPath, relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        return Task.FromResult(File.Exists(fullPath));
    }
}
