using System.Reflection;
using System.Text;
using System.Text.Json;

namespace FlexCms.Framework.Helpers;

/// <summary>
/// Reads files shipped as <c>&lt;EmbeddedResource&gt;</c> inside a module or
/// framework DLL. Pattern of choice for default configs, permission JSON, SQL
/// templates, and prompt files — keeps everything tracked in source control
/// and avoids "is the file deployed?" headaches.
///
/// <para>
/// Resource paths use the dotted form a .NET project emits, e.g.
/// <c>"FlexCms.Sample.Hello.Resources.defaults.json"</c>. The "ends with"
/// overload exists so callers can pass just the relative path
/// (<c>"Resources/defaults.json"</c>) without knowing the assembly namespace.
/// </para>
/// </summary>
public static class FcmsEmbeddedResourceHelper
{
    /// <summary>
    /// Reads the resource whose full manifest name <b>ends with</b>
    /// <paramref name="endsWith"/> from <paramref name="assembly"/>. Returns
    /// <c>null</c> when no resource matches.
    /// </summary>
    public static string? Read(Assembly assembly, string endsWith)
    {
        var name = ResolveName(assembly, endsWith);
        if (name is null) return null;
        using var stream = assembly.GetManifestResourceStream(name);
        if (stream is null) return null;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Async variant of <see cref="Read"/>.
    /// </summary>
    public static async Task<string?> ReadAsync(Assembly assembly, string endsWith, CancellationToken ct = default)
    {
        var name = ResolveName(assembly, endsWith);
        if (name is null) return null;
        using var stream = assembly.GetManifestResourceStream(name);
        if (stream is null) return null;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(ct);
    }

    /// <summary>
    /// Reads the resource and deserializes it as JSON. Returns <c>null</c> when
    /// the resource is missing or the JSON does not deserialize to
    /// <typeparamref name="T"/>.
    /// </summary>
    public static T? ReadJson<T>(Assembly assembly, string endsWith, JsonSerializerOptions? options = null)
    {
        var json = Read(assembly, endsWith);
        if (string.IsNullOrWhiteSpace(json)) return default;
        try { return JsonSerializer.Deserialize<T>(json, options); }
        catch (JsonException) { return default; }
    }

    /// <summary>Async variant of <see cref="ReadJson{T}"/>.</summary>
    public static async Task<T?> ReadJsonAsync<T>(
        Assembly assembly,
        string endsWith,
        JsonSerializerOptions? options = null,
        CancellationToken ct = default)
    {
        var json = await ReadAsync(assembly, endsWith, ct);
        if (string.IsNullOrWhiteSpace(json)) return default;
        try { return JsonSerializer.Deserialize<T>(json, options); }
        catch (JsonException) { return default; }
    }

    /// <summary>
    /// Lists every manifest resource name in <paramref name="assembly"/>. Useful
    /// for debugging "why isn't my resource found".
    /// </summary>
    public static IReadOnlyList<string> ListResources(Assembly assembly)
        => assembly.GetManifestResourceNames();

    // ── internals ─────────────────────────────────────────────────────────

    private static string? ResolveName(Assembly assembly, string endsWith)
    {
        if (string.IsNullOrWhiteSpace(endsWith)) return null;
        // Accept "Resources/foo.json", "Resources.foo.json", or the full
        // manifest name verbatim. Normalize forward / back slashes to dots.
        var token = endsWith.Replace('/', '.').Replace('\\', '.');
        var names = assembly.GetManifestResourceNames();
        return Array.Find(names, n => n.EndsWith(token, StringComparison.OrdinalIgnoreCase));
    }
}
