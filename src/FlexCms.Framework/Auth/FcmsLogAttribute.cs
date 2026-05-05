using FlexCms.Framework.Cms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace FlexCms.Framework.Auth;

/// <summary>
/// Allows a controller action to pass an entity ID to <see cref="FcmsLogAttribute"/>
/// when the ID is only known after the entity is saved (e.g. create actions).
/// </summary>
/// <example>
/// FcmsLogContext.SetEntityId(HttpContext, role.Id);
/// return RedirectToAction(nameof(Index));
/// </example>
public static class FcmsLogContext
{
    internal const string EntityIdKey = "FcmsLog.EntityId";

    public static void SetEntityId(Microsoft.AspNetCore.Http.HttpContext httpContext, Guid id)
        => httpContext.Items[EntityIdKey] = id.ToString();

    public static void SetEntityId(Microsoft.AspNetCore.Http.HttpContext httpContext, string id)
        => httpContext.Items[EntityIdKey] = id;
}

/// <summary>
/// Writes an operation log entry after the action completes successfully.
/// Logs only when the response is a redirect (PRG) or a 2xx JSON result.
/// Entity ID is extracted from the route <c>{id}</c> parameter automatically;
/// pass <paramref name="entityIdParam"/> to use a different route key.
/// </summary>
/// <example>
/// [FcmsLog("users.create", "FcmsUser")]
/// [FcmsLog("users.edit",   "FcmsUser", entityIdParam: "id")]
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class FcmsLogAttribute : Attribute, IFilterFactory, IOrderedFilter
{
    public string Action { get; }
    public string EntityType { get; }
    public string EntityIdParam { get; }
    public string Module { get; }

    public int Order => 100; // run after authorization
    public bool IsReusable => false;

    public FcmsLogAttribute(
        string action,
        string entityType,
        string entityIdParam = "id",
        string module = "core")
    {
        Action = action;
        EntityType = entityType;
        EntityIdParam = entityIdParam;
        Module = module;
    }

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
    {
        var logService = serviceProvider.GetService<IFcmsLogService>();
        return new FcmsLogFilter(Action, EntityType, EntityIdParam, Module, logService);
    }
}

internal sealed class FcmsLogFilter : IAsyncResultFilter
{
    private readonly string _action;
    private readonly string _entityType;
    private readonly string _entityIdParam;
    private readonly string _module;
    private readonly IFcmsLogService? _logService;

    public FcmsLogFilter(
        string action,
        string entityType,
        string entityIdParam,
        string module,
        IFcmsLogService? logService)
    {
        _action = action;
        _entityType = entityType;
        _entityIdParam = entityIdParam;
        _module = module;
        _logService = logService;
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        await next();

        if (_logService is null) return;
        if (!IsSuccess(context.Result)) return;

        // Route param first (edit/delete); fall back to Items set by controller (create)
        var entityId = context.RouteData.Values.TryGetValue(_entityIdParam, out var v)
            ? v?.ToString() ?? ""
            : context.HttpContext.Items[FcmsLogContext.EntityIdKey]?.ToString() ?? "";

        await _logService.LogAsync(
            _action,
            _entityType,
            entityId,
            module: _module,
            ct: context.HttpContext.RequestAborted);
    }

    private static bool IsSuccess(IActionResult result) => result switch
    {
        RedirectToActionResult => true,
        RedirectResult => true,
        JsonResult json => json.StatusCode is null or >= 200 and < 300,
        ObjectResult obj => obj.StatusCode is null or >= 200 and < 300,
        _ => false
    };
}
