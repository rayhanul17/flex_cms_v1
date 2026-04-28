using FlexCms.Framework.Auth;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using UAParser;

namespace FlexCms.Framework.Services;

public class FcmsContextService : IFcmsContextService
{
    private readonly IHttpContextAccessor _httpCtx;
    private static readonly Parser _uaParser = Parser.GetDefault();

    public FcmsContextService(IHttpContextAccessor httpCtx)
    {
        _httpCtx = httpCtx;
    }

    private HttpContext? Http => _httpCtx.HttpContext;
    private ClaimsPrincipal? User => Http?.User;

    public Guid? UserId
    {
        get
        {
            var raw = User?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public string? Username => User?.FindFirstValue(ClaimTypes.Name);
    public string? Email => User?.FindFirstValue(ClaimTypes.Email);
    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;
    public bool IsSuperAdmin => User?.IsInRole(FcmsRoles.SuperAdmin) == true;

    public string IpAddress
        => Http?.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    public string Browser
    {
        get
        {
            var ua = Http?.Request.Headers.UserAgent.ToString();
            if (string.IsNullOrEmpty(ua)) return "unknown";
            var client = _uaParser.Parse(ua);
            return $"{client.UA.Family} {client.UA.Major}".Trim();
        }
    }

    public string Os
    {
        get
        {
            var ua = Http?.Request.Headers.UserAgent.ToString();
            if (string.IsNullOrEmpty(ua)) return "unknown";
            var client = _uaParser.Parse(ua);
            return $"{client.OS.Family} {client.OS.Major}".Trim();
        }
    }
}
