using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Themes;

public sealed class ThemeManager : IThemeManager
{
    private readonly string _themesRoot;
    private readonly ILogger<ThemeManager>? _logger;
    private readonly object _lock = new();
    private List<ThemeManifest> _cache = [];

    public const string DefaultId = "FlexCms.Default";

    public ThemeManager(string themesRoot, ILogger<ThemeManager>? logger = null)
    {
        _themesRoot = themesRoot;
        _logger = logger;
        Refresh();
    }

    public IReadOnlyList<ThemeManifest> All { get { lock (_lock) return _cache.ToArray(); } }

    public ThemeManifest Default { get; } = new()
    {
        Id = DefaultId,
        Name = "FlexCms Default",
        Description = "Built-in Bootstrap 5 default theme. Always available.",
        IsBuiltIn = true,
        SupportsPublic = true,
        SupportsAdmin = true,
        SupportedModes = ["light", "dark", "auto"]
    };

    public ThemeManifest? Get(string themeId)
    {
        if (string.Equals(themeId, DefaultId, StringComparison.OrdinalIgnoreCase)) return Default;
        lock (_lock)
            return _cache.FirstOrDefault(t => string.Equals(t.Id, themeId, StringComparison.OrdinalIgnoreCase));
    }

    public void Refresh()
    {
        var found = new List<ThemeManifest> { Default };
        try
        {
            if (Directory.Exists(_themesRoot))
            {
                foreach (var dir in Directory.EnumerateDirectories(_themesRoot))
                {
                    var manifestPath = Path.Combine(dir, "theme.json");
                    if (!File.Exists(manifestPath)) continue;
                    try
                    {
                        using var stream = File.OpenRead(manifestPath);
                        var manifest = JsonSerializer.Deserialize<ThemeManifest>(stream, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id)) continue;
                        // Skip duplicate ids — first one wins (built-in always has priority).
                        if (found.Any(f => string.Equals(f.Id, manifest.Id, StringComparison.OrdinalIgnoreCase))) continue;
                        found.Add(manifest);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "ThemeManager: failed to load manifest at {Path}", manifestPath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "ThemeManager: filesystem scan failed at {Root}", _themesRoot);
        }

        lock (_lock) _cache = found;
    }
}
