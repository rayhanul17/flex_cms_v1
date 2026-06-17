using FlexCms.Framework.Modules;
using Microsoft.AspNetCore.Hosting;

namespace FlexCms.Framework.Storage;

public sealed class ModuleStorageResolver : IFcmsModuleStorageResolver
{
    private readonly ModuleRegistry _registry;
    private readonly IWebHostEnvironment _env;

    public ModuleStorageResolver(ModuleRegistry registry, IWebHostEnvironment env)
    {
        _registry = registry;
        _env = env;
    }

    public ModuleStorageTarget Resolve(string? moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            // Host fallback — uploads land in {wwwroot}/uploads/ as they did
            // before the module-owned change. Keeps the media library code
            // path unchanged.
            return new ModuleStorageTarget(
                PhysicalDirectory: Path.Combine(_env.WebRootPath, "uploads"),
                PublicUrlBase: "/uploads");
        }

        var module = _registry.FindById(moduleId);
        var slug = moduleId.ToLowerInvariant();

        // Module folder is preferred when discovered, but if the module is
        // packaged-only (no source folder at runtime) we fall back to the
        // host's own /modules/{slug}/uploads/ so the upload still lands on
        // disk and a public URL exists.
        if (module is not null && !string.IsNullOrEmpty(module.FolderPath))
        {
            var moduleWwwroot = Path.Combine(module.FolderPath, "wwwroot");
            return new ModuleStorageTarget(
                PhysicalDirectory: Path.Combine(moduleWwwroot, "uploads"),
                PublicUrlBase: $"/modules/{slug}/uploads");
        }

        return new ModuleStorageTarget(
            PhysicalDirectory: Path.Combine(_env.WebRootPath, "modules", slug, "uploads"),
            PublicUrlBase: $"/modules/{slug}/uploads");
    }
}
