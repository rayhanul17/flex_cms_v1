namespace FlexCms.Framework.Rendering;

/// <summary>
/// Render a Razor view to a string from non-MVC contexts: widgets, email
/// templates, scheduled report bodies. The implementation runs the same
/// view engine the MVC pipeline uses, so partials, tag helpers, and
/// section content all work as in a normal Razor execution.
/// </summary>
public interface IFcmsViewRenderService
{
    /// <param name="viewPath">Either an action-style name (<c>Index</c>) or a relative path (<c>~/Views/Email/Welcome.cshtml</c>).</param>
    /// <param name="model">Model passed as the strongly-typed model.</param>
    Task<string> RenderAsync<TModel>(string viewPath, TModel model, CancellationToken ct = default);
}
