namespace FlexCms.Framework.Cdn;

/// <summary>
/// Stored under settings key <c>cdn:default</c>. Empty <see cref="BaseUrl"/>
/// means CDN is disabled and asset URLs are served from the origin.
/// </summary>
public class CdnSettings
{
    public bool Enabled { get; set; }

    /// <summary>
    /// Base URL like <c>https://cdn.example.com</c> (no trailing slash). Asset
    /// paths are appended directly — uploaded files keep their relative path
    /// (<c>/uploads/abc.jpg</c>) so the resulting CDN URL is
    /// <c>https://cdn.example.com/uploads/abc.jpg</c>.
    /// </summary>
    public string BaseUrl { get; set; } = "";
}

public interface ICdnUrlService
{
    /// <summary>Returns the public URL for <paramref name="relativePath"/> — CDN if enabled, otherwise the path unchanged.</summary>
    Task<string> ResolveAsync(string relativePath, CancellationToken ct = default);
}
