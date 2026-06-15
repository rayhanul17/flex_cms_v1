using FlexCms.Framework.Auth;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using FlexCms.Framework.Models;
using FlexCms.Framework.Mvc;
using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Mvc;
using System.Linq.Expressions;

namespace FlexCms.Host.Controllers.Admin;

/// <summary>
/// Base class for every admin-area controller. Inherits the shared
/// <see cref="BaseFcmsController"/> surface (toasts, AJAX envelope, cache,
/// session, logger) and layers admin-specific concerns on top:
/// the global <see cref="FcmsAuthorizeAttribute"/> gate, a DataTables
/// server-side helper, and lazy access to the audit log + context services.
/// </summary>
[FcmsAuthorize]
public abstract class BaseAdminController : BaseFcmsController
{
    // ── Shorthand for admin-only DI services ──────────────────────────────

    protected IFcmsContextService FcmsContext =>
        HttpContext.RequestServices.GetRequiredService<IFcmsContextService>();

    protected IFcmsLogService OpLog =>
        HttpContext.RequestServices.GetRequiredService<IFcmsLogService>();

    // ── DataTable helper (server-side processing + auto permission flags) ─

    /// <summary>
    /// Build a server-side DataTables JSON response from an EF query.
    ///
    /// <example>
    /// [HttpPost("datatable")]
    /// public Task&lt;IActionResult&gt; DataTable([FromForm] DataTablesRequest req, CancellationToken ct)
    ///     =&gt; DataTableResult(_db.Pages, req,
    ///            select: p =&gt; new { p.Id, p.Title, p.Slug, Status = (int)p.Status, p.UpdatedAt },
    ///            orderColumns: new Expression&lt;Func&lt;FcmsPage, object&gt;&gt;[] {
    ///                p =&gt; p.Title, p =&gt; p.Slug, p =&gt; p.Status, p =&gt; p.UpdatedAt!
    ///            },
    ///            globalSearch: q =&gt; p =&gt; p.Title.Contains(q) || p.Slug.Contains(q),
    ///            permissions: new() { ["canEdit"] = FcmsPermissions.PagesEdit, ["canDelete"] = FcmsPermissions.PagesDelete },
    ///            ct: ct);
    /// </example>
    /// </summary>
    protected async Task<IActionResult> DataTableResult<TEntity, TResult>(
        IQueryable<TEntity> source,
        DataTablesRequest req,
        Expression<Func<TEntity, TResult>> select,
        IReadOnlyList<Expression<Func<TEntity, object>>> orderColumns,
        Func<string, Expression<Func<TEntity, bool>>>? globalSearch = null,
        Dictionary<string, string>? permissions = null,
        CancellationToken ct = default)
        where TEntity : class
    {
        Expression<Func<TEntity, bool>>? searchFilter = null;
        if (globalSearch is not null && !string.IsNullOrWhiteSpace(req.SearchValue))
            searchFilter = globalSearch(req.SearchValue);

        var response = await source.ToDataTableAsync(req, select, searchFilter, orderColumns, ct);

        if (permissions is { Count: > 0 })
        {
            var permService = HttpContext.RequestServices.GetService<IPermissionService>();
            var user = HttpContext.User;
            var isSuperAdmin = user.IsInRole(FcmsRoles.SuperAdmin);

            foreach (var (flagName, permKey) in permissions)
            {
                if (isSuperAdmin) { response.Permissions[flagName] = true; continue; }
                response.Permissions[flagName] = permService is not null
                    && await permService.HasPermissionAsync(user, permKey, ct);
            }
        }

        return Json(response);
    }
}
