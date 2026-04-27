using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace FlexCms.Framework.Db.Ef;

public class EfRepository<T> : IRepository<T> where T : BaseEfEntity
{
    protected readonly DbContext _context;
    protected readonly DbSet<T> _set;

    public EfRepository(DbContext context)
    {
        _context = context;
        _set = context.Set<T>();
    }

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _set.FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted, ct);

    public async Task<List<T>> GetAllAsync(CancellationToken ct = default)
        => await _set.Where(e => !e.IsDeleted).ToListAsync(ct);

    public async Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => await _set.Where(e => !e.IsDeleted).Where(predicate).ToListAsync(ct);

    public async Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => await _set.Where(e => !e.IsDeleted).FirstOrDefaultAsync(predicate, ct);

    public async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => await _set.Where(e => !e.IsDeleted).AnyAsync(predicate, ct);

    public async Task<long> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default)
    {
        var query = _set.Where(e => !e.IsDeleted);
        return predicate is null
            ? await query.LongCountAsync(ct)
            : await query.LongCountAsync(predicate, ct);
    }

    public async Task AddAsync(T entity, CancellationToken ct = default)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await _set.AddAsync(entity, ct);
    }

    public async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        foreach (var e in entities) { e.CreatedAt = now; e.UpdatedAt = now; }
        await _set.AddRangeAsync(entities, ct);
    }

    public Task UpdateAsync(T entity, CancellationToken ct = default)
    {
        entity.UpdatedAt = DateTime.UtcNow;
        _set.Update(entity);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(T entity, CancellationToken ct = default)
    {
        _set.Remove(entity);
        return Task.CompletedTask;
    }

    public Task SoftDeleteAsync(T entity, CancellationToken ct = default)
    {
        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        _set.Update(entity);
        return Task.CompletedTask;
    }
}
