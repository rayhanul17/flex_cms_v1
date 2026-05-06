using System.Diagnostics;
using FlexCms.Host.Models;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => View();

    public IActionResult Privacy() => View();

    /// <summary>Catch-all unhandled-exception handler.</summary>
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
        => View("Error500", new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });

    /// <summary>
    /// Status-code re-execute target — wired by
    /// <c>UseStatusCodePagesWithReExecute</c>. Each known status code maps to
    /// its own styled view; unknown codes fall through to the generic 500
    /// page so we never render an unstyled fallback to end users. The
    /// original status is preserved (important for SEO crawlers + browser
    /// DevTools).
    /// </summary>
    [Route("Home/Error/{statusCode:int}")]
    public IActionResult Error(int statusCode)
    {
        Response.StatusCode = statusCode;
        var vm = new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        };
        return statusCode switch
        {
            401 => View("Error401", vm),
            403 => View("Error403", vm),
            404 => View("Error404", vm),
            _ => View("Error500", vm)
        };
    }
}
