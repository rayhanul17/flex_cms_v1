using FlexCms.Module.Name.Services;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Module.Name.Controllers;

/// <summary>
/// Public-facing controller — JSON endpoint your frontend, mobile app, or other
/// modules can consume without authentication. Delete or extend as needed.
/// </summary>
[Route("api/mod_prefix")]
public class Public__ShortName__Controller : ControllerBase
{
    private readonly __ShortName__Service _service;
    public Public__ShortName__Controller(__ShortName__Service service) => _service = service;

    [HttpGet("")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var items = await _service.GetAllAsync(ct);
        return Ok(items
            .Where(x => x.IsPublished)
            .Select(x => new { x.Id, x.Title, x.Description, x.CreatedAt }));
    }
}
