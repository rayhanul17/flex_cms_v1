using Microsoft.EntityFrameworkCore;

namespace FlexCms.Framework.Modules;

/// <summary>
/// Implement in a module to register that module's EF entities and
/// configuration into <c>FcmsDbContext.OnModelCreating</c>.
///
/// Register in <see cref="IFcmsModule.RegisterServices"/>:
/// <code>services.AddSingleton&lt;IFcmsModelBuilder, MyModelBuilder&gt;();</code>
///
/// The framework calls <see cref="Build"/> for every registered builder
/// during <c>OnModelCreating</c>, so module tables are created by the same
/// migration run as framework tables.
/// </summary>
public interface IFcmsModelBuilder
{
    void Build(ModelBuilder modelBuilder);
}
