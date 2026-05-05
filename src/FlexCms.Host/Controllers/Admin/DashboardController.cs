using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers.Admin;

[Route("admin")]
public class DashboardController : BaseAdminController
{
    [HttpGet("")]
    public IActionResult Index() => View();
}
