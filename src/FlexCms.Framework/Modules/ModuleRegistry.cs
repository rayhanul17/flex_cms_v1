namespace FlexCms.Framework.Modules;

/// <summary>
/// Singleton snapshot of every module the host discovered at startup.
/// Inject this anywhere you need to enumerate or look up modules.
/// </summary>
public sealed class ModuleRegistry
{
    private readonly Dictionary<string, LoadedModule> _byId;

    public ModuleRegistry(IEnumerable<LoadedModule> modules)
    {
        Modules = [.. modules];
        _byId = Modules.ToDictionary(m => m.ModuleId, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<LoadedModule> Modules { get; }

    public LoadedModule? FindById(string moduleId)
        => _byId.TryGetValue(moduleId, out var m) ? m : null;
}
