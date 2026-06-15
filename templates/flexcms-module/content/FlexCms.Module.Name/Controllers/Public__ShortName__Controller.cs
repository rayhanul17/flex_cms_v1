using FlexCms.Framework.Db;
using FlexCms.Module.Name.Data;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Module.Name.Controllers;

/// <summary>
/// Public-facing controller — JSON endpoint your frontend, mobile app, or other
/// modules can consume without authentication. Delete or extend as needed.
/// </summary>
[Route("api/mod_prefix")]
public class Public__ShortName__Controller : ControllerBase
{
    private readonly IRepository<__ShortName__Item> _items;

    public Public__ShortName__Controller(IRepository<__ShortName__Item> items) => _items = items;

    [HttpGet("")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var items = await _items.FindAsync(x => x.IsPublished, ct);
        return Ok(items.Select(x => new
        {
            x.Id,
            x.Title,
            x.Description,
            x.CreatedAt
        }));
    }
}
