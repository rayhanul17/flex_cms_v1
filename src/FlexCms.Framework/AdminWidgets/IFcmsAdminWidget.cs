namespace FlexCms.Framework.AdminWidgets;

/// <summary>
/// A small admin-dashboard tile shipped by a module. Modules register one
/// or more <c>IFcmsAdminWidget</c> implementations in DI; the admin
/// dashboard view component scans them at render time, filters by the
/// current user's permissions, and renders them in <see cref="SortOrder"/>.
///
/// <para>
/// Distinct from <see cref="Widgets.IFcmsWidget"/> — that's for the public
/// site (sidebar / footer); this is the admin dashboard.
/// </para>
///
/// <para>
/// E-commerce module example:
/// <code>
/// services.AddScoped&lt;IFcmsAdminWidget, TodayOrdersWidget&gt;();
/// </code>
/// </para>
/// </summary>
public interface IFcmsAdminWidget
{
    /// <summary>Stable identifier — used in dashboard layout JSON.</summary>
    string Id { get; }

    string DisplayName { get; }

    /// <summary>Bootstrap-icons class (e.g. <c>"bi bi-cart"</c>).</summary>
    string Icon { get; }

    /// <summary>Render order on the dashboard.</summary>
    int SortOrder { get; }

    /// <summary>
    /// Permission key required to view this widget. Empty/null = visible to
    /// any admin who can see the dashboard. The dashboard view component
    /// runs the check via <c>IPermissionService</c>.
    /// </summary>
    string? RequiredPermission { get; }

    /// <summary>
    /// Render the widget body — returns ready-to-inject HTML. Keep it small
    /// (one or two stats + an icon); use a CTA link for drill-down.
    /// </summary>
    Task<string> RenderAsync(AdminWidgetContext ctx, CancellationToken ct = default);
}

/// <summary>Inputs the widget renderer needs.</summary>
public sealed record AdminWidgetContext(Guid? UserId, string LanguageCode);
