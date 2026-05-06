using FlexCms.Framework.Auth;
using FlexCms.Framework.Cms;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using NSubstitute;
using Xunit;

namespace FlexCms.Tests.Unit.Phase3;

public class FcmsLogFilterTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (ResultExecutingContext ctx, ResultExecutionDelegate next) BuildContext(
        IActionResult result,
        RouteData? routeData = null,
        Dictionary<object, object?>? items = null)
    {
        var httpContext = new DefaultHttpContext();
        if (items is not null)
            foreach (var kv in items)
                httpContext.Items[kv.Key] = kv.Value;

        var actionContext = new ActionContext(
            httpContext,
            routeData ?? new RouteData(),
            new ActionDescriptor());

        var ctx = new ResultExecutingContext(actionContext, [], result, new object());
        ResultExecutionDelegate next = () => Task.FromResult(
            new ResultExecutedContext(actionContext, [], result, new object()));
        return (ctx, next);
    }

    private static FcmsLogFilter MakeFilter(
        IFcmsLogService? logService,
        string entityIdParam = "id")
        => new("test.action", "TestEntity", entityIdParam, "core", logService);

    // ── Success cases ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Redirect_logs_entity_id_from_route()
    {
        var logService = Substitute.For<IFcmsLogService>();
        var entityId = Guid.NewGuid();
        var route = new RouteData();
        route.Values["id"] = entityId.ToString();

        var (ctx, next) = BuildContext(new RedirectToActionResult("Index", "Home", null), route);
        await MakeFilter(logService).OnResultExecutionAsync(ctx, next);

        await logService.Received(1).LogAsync(
            "test.action", "TestEntity", entityId.ToString(),
            value: Arg.Any<object?>(), module: "core",
            severity: Arg.Any<FcmsLogSeverity>(), ct: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Redirect_logs_entity_id_from_HttpContext_Items_when_no_route_param()
    {
        var logService = Substitute.For<IFcmsLogService>();
        var entityId = Guid.NewGuid();
        var items = new Dictionary<object, object?> { [FcmsLogContext.EntityIdKey] = entityId.ToString() };

        var (ctx, next) = BuildContext(new RedirectToActionResult("Index", "Home", null), items: items);
        await MakeFilter(logService).OnResultExecutionAsync(ctx, next);

        await logService.Received(1).LogAsync(
            "test.action", "TestEntity", entityId.ToString(),
            value: Arg.Any<object?>(), module: "core",
            severity: Arg.Any<FcmsLogSeverity>(), ct: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Route_param_takes_precedence_over_HttpContext_Items()
    {
        var logService = Substitute.For<IFcmsLogService>();
        var routeId = Guid.NewGuid();
        var itemsId = Guid.NewGuid();

        var route = new RouteData();
        route.Values["id"] = routeId.ToString();
        var items = new Dictionary<object, object?> { [FcmsLogContext.EntityIdKey] = itemsId.ToString() };

        var (ctx, next) = BuildContext(new RedirectToActionResult("Index", "Home", null), route, items);
        await MakeFilter(logService).OnResultExecutionAsync(ctx, next);

        await logService.Received(1).LogAsync(
            "test.action", "TestEntity", routeId.ToString(),
            value: Arg.Any<object?>(), module: "core",
            severity: Arg.Any<FcmsLogSeverity>(), ct: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OkJson_2xx_logs_entry()
    {
        var logService = Substitute.For<IFcmsLogService>();
        var (ctx, next) = BuildContext(new JsonResult(new { }) { StatusCode = 200 });

        await MakeFilter(logService).OnResultExecutionAsync(ctx, next);

        await logService.Received(1).LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            value: Arg.Any<object?>(), module: Arg.Any<string>(),
            severity: Arg.Any<FcmsLogSeverity>(), ct: Arg.Any<CancellationToken>());
    }

    // ── No-log cases ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ViewResult_does_not_log()
    {
        var logService = Substitute.For<IFcmsLogService>();
        var (ctx, next) = BuildContext(new ViewResult());

        await MakeFilter(logService).OnResultExecutionAsync(ctx, next);

        await logService.DidNotReceive().LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            value: Arg.Any<object?>(), module: Arg.Any<string>(),
            severity: Arg.Any<FcmsLogSeverity>(), ct: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FailJson_4xx_does_not_log()
    {
        var logService = Substitute.For<IFcmsLogService>();
        var (ctx, next) = BuildContext(new JsonResult(new { isSuccess = false }) { StatusCode = 400 });

        await MakeFilter(logService).OnResultExecutionAsync(ctx, next);

        await logService.DidNotReceive().LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            value: Arg.Any<object?>(), module: Arg.Any<string>(),
            severity: Arg.Any<FcmsLogSeverity>(), ct: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Null_logService_does_not_throw()
    {
        var (ctx, next) = BuildContext(new RedirectToActionResult("Index", "Home", null));
        var ex = await Record.ExceptionAsync(() => MakeFilter(null).OnResultExecutionAsync(ctx, next));
        Assert.Null(ex);
    }

    // ── Value snapshot ────────────────────────────────────────────────────────

    [Fact]
    public async Task Value_set_via_FcmsLogContext_is_passed_to_LogAsync()
    {
        var logService = Substitute.For<IFcmsLogService>();
        var snapshot = new { Title = "Hello", Slug = "hello" };
        var items = new Dictionary<object, object?> { [FcmsLogContext.ValueKey] = snapshot };

        var (ctx, next) = BuildContext(new RedirectToActionResult("Index", "Home", null), items: items);
        await MakeFilter(logService).OnResultExecutionAsync(ctx, next);

        await logService.Received(1).LogAsync(
            "test.action", "TestEntity", "",
            value: snapshot,
            module: "core",
            severity: Arg.Any<FcmsLogSeverity>(),
            ct: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Value_omitted_when_FcmsLogContext_SetValue_not_called()
    {
        var logService = Substitute.For<IFcmsLogService>();

        var (ctx, next) = BuildContext(new RedirectToActionResult("Index", "Home", null));
        await MakeFilter(logService).OnResultExecutionAsync(ctx, next);

        await logService.Received(1).LogAsync(
            action: Arg.Any<string>(),
            entityType: Arg.Any<string>(),
            entityId: Arg.Any<string>(),
            value: Arg.Is<object?>(v => v == null),
            module: Arg.Any<string>(),
            severity: Arg.Any<FcmsLogSeverity>(),
            ct: Arg.Any<CancellationToken>());
    }

    // ── FcmsLogContext helper ─────────────────────────────────────────────────

    [Fact]
    public void SetEntityId_guid_stores_string_in_Items()
    {
        var httpContext = new DefaultHttpContext();
        var id = Guid.NewGuid();
        FcmsLogContext.SetEntityId(httpContext, id);
        Assert.Equal(id.ToString(), httpContext.Items[FcmsLogContext.EntityIdKey]);
    }

    [Fact]
    public void SetEntityId_string_stores_value_in_Items()
    {
        var httpContext = new DefaultHttpContext();
        FcmsLogContext.SetEntityId(httpContext, "custom-id-123");
        Assert.Equal("custom-id-123", httpContext.Items[FcmsLogContext.EntityIdKey]);
    }

    [Fact]
    public void SetValue_stores_object_in_Items()
    {
        var httpContext = new DefaultHttpContext();
        var snapshot = new { Title = "X" };
        FcmsLogContext.SetValue(httpContext, snapshot);
        Assert.Same(snapshot, httpContext.Items[FcmsLogContext.ValueKey]);
    }
}
