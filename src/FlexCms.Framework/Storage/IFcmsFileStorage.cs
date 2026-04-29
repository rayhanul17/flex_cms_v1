namespace FlexCms.Framework.Storage;

public interface IFcmsFileStorage
{
    /// <summary>Save bytes to the given relative path (from storage root) and return the public URL.</summary>
    Task<string> SaveAsync(string relativePath, Stream content, CancellationToken ct = default);

    /// <summary>Delete the file at the given relative path. No-op if the file does not exist.</summary>
    Task DeleteAsync(string relativePath, CancellationToken ct = default);

    /// <summary>Check whether a file exists at the given relative path.</summary>
    Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default);
}
