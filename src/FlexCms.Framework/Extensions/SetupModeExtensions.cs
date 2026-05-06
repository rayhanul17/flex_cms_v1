using FlexCms.Framework.Setup;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace FlexCms.Framework.Extensions;

public static class SetupModeExtensions
{
    public static IServiceCollection AddSetupModeServices(
        this IServiceCollection services,
        string appDataPath)
    {
        services.AddControllersWithViews();
        services.AddSession(o => o.IdleTimeout = TimeSpan.FromMinutes(30));
        services.AddDistributedMemoryCache();

        // Match the production-mode antiforgery header so setup-wizard JS
        // (Setup/Index.cshtml, Setup/Complete.cshtml) sends the same
        // X-FlexCms-Csrf header it would in production. Without this, the
        // wizard's [Test Connection] / [Save] AJAX calls fail with 400.
        services.AddAntiforgery(o => o.HeaderName = "X-FlexCms-Csrf");

        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(appDataPath, "keys")))
            .SetApplicationName("FlexCms");

        services.AddSingleton<SetupHelper>(sp =>
        {
            var dp = sp.GetRequiredService<IDataProtectionProvider>();
            return new SetupHelper(dp, appDataPath);
        });

        return services;
    }
}
