namespace FlexCms.Framework.Helpers;

/// <summary>
/// Parsed MVC route extracted from a raw URL path.
/// </summary>
public sealed record FcmsRouteParts(string? Area, string Controller, string Action, IReadOnlyList<string> ExtraSegments);

/// <summary>
/// URL parsing helpers used by audit logging, menu builders, and permission
/// resolvers — anywhere the framework needs to reverse-engineer the route
/// from a raw href without going through MVC's IUrlHelper.
/// </summary>
public static class FcmsUrlHelper
{
    /// <summary>
    /// Parse a URL path into <c>(area?, controller, action, extra)</c>. Recognises
    /// the conventional 2-, 3-, and 4-segment MVC URLs:
    /// <list type="bullet">
    /// <item><c>/Controller</c> → controller (action = "Index")</item>
    /// <item><c>/Controller/Action</c></item>
    /// <item><c>/Area/Controller/Action</c> when the first segment is in <paramref name="knownAreas"/></item>
    /// <item><c>/Controller/Action/id/extra…</c> — extra segments returned as <c>ExtraSegments</c></item>
    /// </list>
    ///
    /// <para>
    /// Query strings, fragments, and leading scheme/host are stripped before
    /// parsing. Returns <c>null</c> when the path is empty or only "/".
    /// </para>
    /// </summary>
    public static FcmsRouteParts? Parse(string? url, IReadOnlySet<string>? knownAreas = null)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var path = StripSchemeHostAndQuery(url);
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return null;

        string? area = null;
        var start = 0;
        if (knownAreas is not null && segments.Length >= 2 && knownAreas.Contains(segments[0]))
        {
            area = segments[0];
            start = 1;
        }

        var controller = segments[start];
        var action = segments.Length > start + 1 ? segments[start + 1] : "Index";
        var extras = segments.Length > start + 2
            ? segments[(start + 2)..]
            : Array.Empty<string>();

        return new FcmsRouteParts(area, controller, action, extras);
    }

    /// <summary>
    /// Shortcut overload: returns just the controller and action, ignoring areas
    /// and any trailing segments. Returns <c>(null, null)</c> when the URL has
    /// no controller segment.
    /// </summary>
    public static (string? Controller, string Action) GetControllerAction(string? url)
    {
        var parts = Parse(url);
        return (parts?.Controller, parts?.Action ?? "Index");
    }

    /// <summary>
    /// Shortcut overload: returns the area / controller / action tuple. Areas
    /// are detected only when the caller supplies <paramref name="knownAreas"/>.
    /// </summary>
    public static (string? Area, string? Controller, string Action) GetControllerActionArea(
        string? url,
        IReadOnlySet<string>? knownAreas = null)
    {
        var parts = Parse(url, knownAreas);
        return (parts?.Area, parts?.Controller, parts?.Action ?? "Index");
    }

    /// <summary>
    /// Returns the segment count after stripping scheme/host/query. Useful when
    /// you only need to check "is this a root URL" vs. "is this a deep link".
    /// </summary>
    public static int SegmentCount(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return 0;
        var path = StripSchemeHostAndQuery(url);
        return path.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>
    /// Combines a base URL and a relative path, collapsing any duplicate
    /// slashes between them. Either argument may be null/empty.
    /// </summary>
    public static string Combine(string? baseUrl, string? relative)
    {
        if (string.IsNullOrEmpty(baseUrl)) return relative ?? "";
        if (string.IsNullOrEmpty(relative)) return baseUrl;
        return baseUrl.TrimEnd('/') + "/" + relative.TrimStart('/');
    }

    /// <summary>
    /// True when the value is a valid absolute http(s) URL.
    /// </summary>
    public static bool IsAbsoluteHttpUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);


    private static string StripSchemeHostAndQuery(string url)
    {
        var path = url;

        // Strip scheme://host
        if (Uri.TryCreate(path, UriKind.Absolute, out var abs))
            path = abs.AbsolutePath;

        // Strip query + fragment
        var q = path.IndexOf('?');
        if (q >= 0) path = path[..q];
        var f = path.IndexOf('#');
        if (f >= 0) path = path[..f];

        return path;
    }
}
