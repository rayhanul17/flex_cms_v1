using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;

namespace FlexCms.Framework.Cdn;

public sealed class AssetVersionService : IAssetVersionService
{
    private readonly IWebHostEnvironment _env;
    private readonly ConcurrentDictionary<string, (DateTime Stamp, string Hash)> _cache = new();

    public AssetVersionService(IWebHostEnvironment env) => _env = env;

    public string Versioned(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return relativePath ?? "";
        // External URLs and data URIs pass through unchanged.
        if (relativePath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return relativePath;

        var trimmed = relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.Combine(_env.WebRootPath ?? "", trimmed);
        if (!File.Exists(fullPath)) return relativePath;

        var lastWrite = File.GetLastWriteTimeUtc(fullPath);
        if (_cache.TryGetValue(fullPath, out var entry) && entry.Stamp == lastWrite)
            return AppendHash(relativePath, entry.Hash);

        var hash = ComputeHash(fullPath);
        _cache[fullPath] = (lastWrite, hash);
        return AppendHash(relativePath, hash);
    }

    private static string ComputeHash(string fullPath)
    {
        using var stream = File.OpenRead(fullPath);
        var bytes = SHA256.HashData(stream);
        // 8 chars = 32 bits — plenty for cache-busting; full SHA would just bloat URLs.
        return Convert.ToHexString(bytes).ToLowerInvariant()[..8];
    }

    private static string AppendHash(string path, string hash)
        => path.Contains('?', StringComparison.Ordinal) ? $"{path}&v={hash}" : $"{path}?v={hash}";
}
