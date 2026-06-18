using FlexCms.Framework.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace FlexCms.Framework.Extensions;

public static class FcmsModuleStaticFilesExtensions
{
    /// <summary>
    /// Mount each module's own <c>wwwroot/</c> folder at
    /// <c>/modules/{module-id-lowercase}/</c>. Lets each module ship its
    /// own CSS / JS / images / uploaded files without anything needing to
    /// be copied into the host's wwwroot at activation time.
    ///
    /// <para>
    /// Call AFTER <c>app.UseStaticFiles()</c> so the host's own static
    /// files (admin shell assets, media library) still take precedence.
    /// </para>
    /// </summary>
    public static IApplicationBuilder UseFcmsModuleStaticFiles(this IApplicationBuilder app)
    {
        var registry = app.ApplicationServices.GetService<ModuleRegistry>();
        if (registry is null) return app;

        var provider = new FileExtensionContentTypeProvider();
        foreach (var module in registry.Modules)
        {
            if (string.IsNullOrEmpty(module.FolderPath)) continue;
            var moduleWwwroot = Path.Combine(module.FolderPath, "wwwroot");
            // Ensure the folder exists so PhysicalFileProvider binds successfully
            // even for modules that haven't shipped any static assets yet —
            // uploads will land here later.
            Directory.CreateDirectory(moduleWwwroot);

            var slug = module.ModuleId.ToLowerInvariant();
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(moduleWwwroot),
                RequestPath = $"/modules/{slug}",
                ContentTypeProvider = provider,
                // Defence-in-depth: if a malformed module ZIP somehow lands a
                // file with no recognised content type under wwwroot/ (e.g.
                // .env, .key, .pem), ServeUnknownFileTypes=false means the
                // static-file middleware returns 404 instead of serving the
                // bytes with application/octet-stream. The upload validator
                // already blocks these by extension; this is the second wall.
                ServeUnknownFileTypes = false,
            });
        }
        return app;
    }
}
