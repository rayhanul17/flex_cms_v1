namespace FlexCms.Framework.Cdn;

/// <summary>
/// Computes a stable cache-busting hash for static asset files. Renders out as
/// <c>/css/site.css?v=a1b2c3d4</c>. The hash is computed once per file then
/// cached in-memory (keyed by absolute path) — file edits invalidate the
/// cache via last-write timestamp.
/// </summary>
public interface IAssetVersionService
{
    /// <summary>
    /// Append <c>?v=hash</c> to <paramref name="relativePath"/>. If the file
    /// can't be located, returns the path unchanged so a missing asset
    /// doesn't break the page render.
    /// </summary>
    string Versioned(string relativePath);
}
