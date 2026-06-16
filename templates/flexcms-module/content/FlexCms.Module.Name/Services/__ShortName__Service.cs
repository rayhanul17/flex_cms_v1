using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Modules;
using FlexCms.Framework.Modules.Attributes;
using FlexCms.Module.Name.Data;
using Microsoft.EntityFrameworkCore;

namespace FlexCms.Module.Name.Services;

/// <summary>
/// Module-owned service. Mirrors host services (CategoryService etc.) by
/// depending on the framework's generic <see cref="IRepository{T}"/> and
/// <see cref="EfRepository{T}"/> abstractions — but because the entity lives
/// in this module's own <see cref="__ShortName__DbContext"/> (not host DI),
/// the service rebuilds the context + repository per request from
/// <see cref="ModuleActivationOptions"/>. The CRUD shape stays identical to
/// what a host author would write.
/// </summary>
[FcmsScoped]
public class __ShortName__Service
{
    private readonly ModuleActivationOptions _opts;
    public __ShortName__Service(ModuleActivationOptions opts) => _opts = opts;

    private (__ShortName__DbContext db, IRepository<__ShortName__Item> repo) Open()
    {
        var db = (__ShortName__DbContext)new __ShortName__Module().CreateMigrationContext(_opts.ConnectionString, _opts.Provider)!;
        var repo = new EfRepository<__ShortName__Item>(db);
        return (db, repo);
    }

    public async Task<List<__ShortName__Item>> GetAllAsync(
        CancellationToken ct = default,
        bool includeDeleted = false,
        bool includeInactive = true)
    {
        var (db, repo) = Open();
        await using (db)
            return (await repo.FindAsync(x => true, ct,
                        includeDeleted: includeDeleted,
                        includeInactive: includeInactive))
                .OrderByDescending(x => x.CreatedAt).ToList();
    }

    public async Task<__ShortName__Item?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var (db, repo) = Open();
        await using (db) return await repo.GetByIdAsync(id, ct);
    }

    public async Task<__ShortName__Item> CreateAsync(__ShortName__Item model, CancellationToken ct = default)
    {
        var (db, repo) = Open();
        await using (db)
        {
            if (model.Id == Guid.Empty) model.Id = Guid.NewGuid();
            await repo.AddAsync(model, ct);
            await db.SaveChangesAsync(ct);
        }
        return model;
    }

    public async Task<bool> UpdateAsync(Guid id, string title, string description, bool isPublished, CancellationToken ct = default)
    {
        var (db, repo) = Open();
        await using (db)
        {
            var row = await repo.GetByIdAsync(id, ct);
            if (row is null) return false;
            row.Title = title.Trim();
            row.Description = description?.Trim() ?? "";
            row.IsPublished = isPublished;
            await repo.UpdateAsync(row, ct);
            await db.SaveChangesAsync(ct);
            return true;
        }
    }

    public async Task<__ShortName__Item?> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var (db, repo) = Open();
        await using (db)
        {
            var row = await repo.GetByIdAsync(id, ct);
            if (row is null) return null;
            await repo.SoftDeleteAsync(row, ct);
            await db.SaveChangesAsync(ct);
            return row;
        }
    }
}
