namespace FlexCms.Framework.Widgets;

/// <summary>
/// A widget is a self-contained UI fragment a module ships and the host
/// renders into a named "zone" (e.g. <c>Sidebar</c>, <c>BeforeBodyEnd</c>,
/// <c>DashboardCards</c>). Widgets register themselves via DI and the
/// <see cref="IFcmsWidgetManager"/> looks them up by id when a placement
/// row points at them.
/// </summary>
public interface IFcmsWidget
{
    /// <summary>Stable id — used as the FK from <c>FcmsWidgetPlacement</c>. Convention: <c>{module}.{name}</c>.</summary>
    string Id { get; }

    /// <summary>Human-readable name shown in the admin Widget Manager.</summary>
    string DisplayName { get; }

    /// <summary>Optional description shown in the admin Widget Manager.</summary>
    string? Description { get; }

    /// <summary>
    /// Render the widget's HTML. <paramref name="configJson"/> is the per-
    /// placement configuration JSON (or empty); the widget owns the schema.
    /// Implementations should handle missing/invalid config without throwing.
    /// </summary>
    Task<string> RenderAsync(string? configJson, IServiceProvider services, CancellationToken ct = default);
}
