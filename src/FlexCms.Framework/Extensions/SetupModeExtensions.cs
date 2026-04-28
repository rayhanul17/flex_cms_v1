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

        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(appDataPath, "keys")))
            .SetApplicationName("FlexCms");

        services.AddScoped<SetupHelper>(sp =>
        {
            var dp = sp.GetRequiredService<IDataProtectionProvider>();
            return new SetupHelper(dp, appDataPath);
        });

        return services;
    }
}
