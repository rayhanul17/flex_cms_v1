using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Net;

namespace FlexCms.Framework.Middleware;

public class IpFilterMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IpFilterOptions _options;

    public IpFilterMiddleware(RequestDelegate next, IOptions<IpFilterOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_options.EnforceIpFilter && _options.AllowedIps.Length > 0)
        {
            var remoteIp = context.Connection.RemoteIpAddress;
            if (remoteIp is null || !_options.AllowedIps.Any(ip =>
                IPAddress.TryParse(ip, out var allowed) && allowed.Equals(remoteIp)))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }

        await _next(context);
    }
}

public class IpFilterOptions
{
    public bool EnforceIpFilter { get; set; }
    public string[] AllowedIps { get; set; } = [];
}
