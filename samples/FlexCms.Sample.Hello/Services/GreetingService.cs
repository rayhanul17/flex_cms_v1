using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Modules;
using FlexCms.Framework.Modules.Attributes;
using FlexCms.Sample.Hello.Data;
using Microsoft.EntityFrameworkCore;

namespace FlexCms.Sample.Hello.Services;

/// <summary>
/// Module-owned service that mirrors host services like <c>CategoryService</c>:
/// it depends on the generic <see cref="IRepository{T}"/> + <see cref="IFcmsUnitOfWork"/>
/// abstractions instead of touching DbContext directly. Because HelloGreeting
/// lives in the module's own <see cref="HelloDbContext"/> (not host DI), this
/// service rebuilds the context + repository per request from
/// <see cref="ModuleActivationOptions"/>. The CRUD shape is identical to a
/// "regular" host service — which is the whole point: the abstractions are
/// generic enough that a module author writes the same code a host author
/// writes.
/// </summary>
[FcmsScoped]
public class GreetingService
{
    private readonly ModuleActivationOptions _opts;
    public GreetingService(ModuleActivationOptions opts) => _opts = opts;

    private (HelloDbContext db, IRepository<HelloGreeting> repo, EfDbUnitOfWork uow) Open()
    {
        var db = (HelloDbContext)new HelloModule().CreateMigrationContext(_opts.ConnectionString, _opts.Provider)!;
        var repo = new EfRepository<HelloGreeting>(db);
        var uow = new EfDbUnitOfWork(db);
        return (db, repo, uow);
    }

    public async Task<List<HelloGreeting>> GetAllAsync(
        CancellationToken ct = default,
        bool includeDeleted = false,
        bool includeInactive = true)
    {
        var (db, repo, _) = Open();
        await using (db)
            return (await repo.FindAsync(g => true, ct,
                        includeDeleted: includeDeleted,
                        includeInactive: includeInactive))
                .OrderByDescending(g => g.CreatedAt).ToList();
    }

    public async Task<HelloGreeting?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var (db, repo, _) = Open();
        await using (db) return await repo.GetByIdAsync(id, ct);
    }

    public async Task<HelloGreeting> CreateAsync(HelloGreeting model, CancellationToken ct = default)
    {
        var (db, repo, uow) = Open();
        await using (db)
        {
            if (model.Id == Guid.Empty) model.Id = Guid.NewGuid();
            await repo.AddAsync(model, ct);
            await uow.SaveChangesAsync(ct);
        }
        return model;
    }

    public async Task<bool> UpdateAsync(Guid id, string audience, string message, CancellationToken ct = default)
    {
        var (db, repo, uow) = Open();
        await using (db)
        {
            var row = await repo.GetByIdAsync(id, ct);
            if (row is null) return false;
            row.Audience = audience.Trim();
            row.Message = message.Trim();
            await repo.UpdateAsync(row, ct);
            await uow.SaveChangesAsync(ct);
            return true;
        }
    }

    public async Task<HelloGreeting?> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var (db, repo, uow) = Open();
        await using (db)
        {
            var row = await repo.GetByIdAsync(id, ct);
            if (row is null) return null;
            await repo.SoftDeleteAsync(row, ct);
            await uow.SaveChangesAsync(ct);
            return row;
        }
    }
}

/// <summary>
/// Tiny IFcmsUnitOfWork-equivalent over the module's own DbContext —
/// keeps GreetingService's signature identical to a host service that takes
/// an IFcmsUnitOfWork. Module's own context isn't registered in host DI, so we
/// can't take IFcmsUnitOfWork directly (it's wired to FcmsDbContext).
/// </summary>
internal sealed class EfDbUnitOfWork
{
    private readonly DbContext _db;
    public EfDbUnitOfWork(DbContext db) => _db = db;
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
