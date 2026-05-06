using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using FlexCms.Host.Models;

namespace FlexCms.Host.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    [Route("Home/Error/{statusCode}")]
    public IActionResult Error(int statusCode)
    {
        if (statusCode == 404)
            return View("Error404");

        return View("Error");
    }
}
