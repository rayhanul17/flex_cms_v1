using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FlexCms.Sample.Hello.Data;

/// <summary>
/// Design-time factory used by the EF tooling (<c>dotnet ef migrations add ...</c>)
/// when generating migration files. Runtime activation never goes through this —
/// the framework's <see cref="HelloModule.CreateMigrationContext"/> constructs
/// the same context against the configured production connection string.
///
/// <para>
/// We pick MySQL here because it's the project's default development backend.
/// The generated SQL is functionally identical across providers; if you need
/// to regenerate against a different provider, edit this factory locally.
/// </para>
/// </summary>
public sealed class HelloDbContextDesignFactory : IDesignTimeDbContextFactory<HelloDbContext>
{
    public HelloDbContext CreateDbContext(string[] args)
    {
        // Connection string is only used to satisfy ServerVersion.AutoDetect;
        // EF tooling doesn't actually connect during `migrations add`.
        const string ConnStr = "Server=localhost;Port=3306;Database=flexcms;User=root;Password=Dev@123456;";

        var options = new DbContextOptionsBuilder<HelloDbContext>()
            .UseMySql(ConnStr, ServerVersion.AutoDetect(ConnStr))
            .Options;

        return new HelloDbContext(options);
    }
}
