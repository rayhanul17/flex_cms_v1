using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace FlexCms.Framework.Rendering;

/// <summary>
/// Renders Razor views to a string by faking up an ActionContext +
/// ViewContext from a fresh DefaultHttpContext. Works whether or not the
/// caller is inside an MVC request.
///
/// <para>
/// View resolution order: <see cref="ICompositeViewEngine.GetView"/> if
/// <paramref name="viewPath"/> looks like a relative file path, then
/// <see cref="ICompositeViewEngine.FindView"/> for action-style names.
/// </para>
/// </summary>
public sealed class FcmsViewRenderService : IFcmsViewRenderService
{
    private readonly ICompositeViewEngine _viewEngine;
    private readonly ITempDataProvider _tempDataProvider;
    private readonly IServiceProvider _serviceProvider;

    public FcmsViewRenderService(
        ICompositeViewEngine viewEngine,
        ITempDataProvider tempDataProvider,
        IServiceProvider serviceProvider)
    {
        _viewEngine = viewEngine;
        _tempDataProvider = tempDataProvider;
        _serviceProvider = serviceProvider;
    }

    public async Task<string> RenderAsync<TModel>(string viewPath, TModel model, CancellationToken ct = default)
    {
        var actionContext = new ActionContext(
            new DefaultHttpContext { RequestServices = _serviceProvider },
            new RouteData(),
            new ActionDescriptor());

        var view = FindView(actionContext, viewPath);

        await using var sw = new StringWriter();
        var viewData = new ViewDataDictionary<TModel>(
            new EmptyModelMetadataProvider(),
            new ModelStateDictionary())
        { Model = model };

        var tempData = new TempDataDictionary(actionContext.HttpContext, _tempDataProvider);

        var viewContext = new ViewContext(
            actionContext, view, viewData, tempData, sw,
            new HtmlHelperOptions());

        await view.RenderAsync(viewContext);
        return sw.ToString();
    }

    private IView FindView(ActionContext ctx, string viewPath)
    {
        // Prefer GetView for explicit paths (anything starting with "/" or "~/")
        // and FindView for action-relative names.
        var byPath = viewPath.StartsWith('/') || viewPath.StartsWith("~/", StringComparison.Ordinal);

        var result = byPath
            ? _viewEngine.GetView(executingFilePath: null, viewPath: viewPath, isMainPage: true)
            : _viewEngine.FindView(ctx, viewPath, isMainPage: true);

        if (!result.Success)
            throw new InvalidOperationException(
                $"View '{viewPath}' was not found. Searched: {string.Join(", ", result.SearchedLocations ?? [])}");

        return result.View!;
    }
}
