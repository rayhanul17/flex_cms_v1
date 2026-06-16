namespace FlexCms.Framework.Cms;

public interface IFcmsLogService
{
    /// <summary>
    /// Write an audit log entry for an entity operation.
    ///
    /// <para><b>Pass the full entity in <paramref name="value"/></b> — the JSON
    /// serializer (<see cref="FcmsLogJsonResolver"/>) automatically strips:
    /// navigation properties (refs + collections), ASP.NET Identity sensitive
    /// fields (PasswordHash, SecurityStamp, etc.), and any property marked
    /// with <see cref="FcmsLogIgnoreAttribute"/>. No need to build anonymous
    /// projections like <c>new { entity.Foo, entity.Bar }</c>.
    /// </para>
    ///
    /// <example>
    /// // From a service:
    /// await _audit.LogAsync(FcmsAuditActions.PostCreated, nameof(FcmsPost),
    ///     post.Id.ToString(), value: post, ct: ct);
    ///
    /// // From a controller using [FcmsLog] attribute:
    /// FcmsLogContext.SetValue(HttpContext, role);
    /// </example>
    /// </summary>
    Task LogAsync(
        string action,
        string entityType,
        string entityId,
        object? value = null,
        string module = "core",
        FcmsLogSeverity severity = FcmsLogSeverity.Info,
        CancellationToken ct = default);

    /// <summary>
    /// Moves all logs older than <paramref name="age"/> to the archive table
    /// (hard delete from main). Returns the number of rows actually moved so
    /// callers can surface "0 archived" vs "12 archived" honestly in the UI.
    /// </summary>
    Task<int> ArchiveOlderThanAsync(TimeSpan age, CancellationToken ct = default);

    /// <summary>Hard-deletes ALL records from the archive table.</summary>
    Task ClearArchiveAsync(CancellationToken ct = default);

    Task<IReadOnlyList<FcmsLog>> GetRecentAsync(int count = 100, CancellationToken ct = default);
    Task<IReadOnlyList<FcmsLogArchive>> GetArchiveAsync(int count = 100, CancellationToken ct = default);
}
