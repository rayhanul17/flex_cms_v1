using System.Linq.Expressions;
using FlexCms.Framework.Models;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver.Linq;

namespace FlexCms.Framework.Db;

/// <summary>
/// One-call helper that turns a <see cref="DataTablesRequest"/> + a base
/// <see cref="IQueryable{T}"/> into a paginated, sorted, searched
/// <see cref="DataTablesResponse{T}"/>. Intended to be called from
/// <c>BaseAdminController.DataTableResult</c>.
///
/// <para>
/// <b>Storage-agnostic.</b> Detects whether <paramref name="source"/> is an EF
/// queryable or a Mongo queryable and dispatches to the right async helper.
/// EF Core and MongoDB.Driver each define their own <c>CountAsync</c> /
/// <c>ToListAsync</c> extension methods on <see cref="IQueryable{T}"/> — naïvely
/// calling either one against the wrong provider throws at runtime.
/// </para>
/// </summary>
public static class DataTableQueryExtensions
{
    public static async Task<DataTablesResponse<TResult>> ToDataTableAsync<TEntity, TResult>(
        this IQueryable<TEntity> source,
        DataTablesRequest req,
        Expression<Func<TEntity, TResult>> select,
        Expression<Func<TEntity, bool>>? globalSearchFilter,
        IReadOnlyList<Expression<Func<TEntity, object>>> orderColumns,
        CancellationToken ct = default)
        where TEntity : class
    {
        var recordsTotal = await CountAsync(source, ct);

        var filtered = source;
        if (!string.IsNullOrWhiteSpace(req.SearchValue) && globalSearchFilter is not null)
            filtered = filtered.Where(globalSearchFilter);

        var recordsFiltered = await CountAsync(filtered, ct);

        // Apply ordering — fall back to first column ascending if index out of range
        if (orderColumns.Count > 0)
        {
            var idx = Math.Clamp(req.OrderColumnIndex, 0, orderColumns.Count - 1);
            var orderExpr = orderColumns[idx];
            filtered = req.IsDescending
                ? filtered.OrderByDescending(orderExpr)
                : filtered.OrderBy(orderExpr);
        }

        var page = filtered
            .Skip(req.Start)
            .Take(req.Length > 0 ? req.Length : int.MaxValue)
            .Select(select);

        var data = await ToListAsync(page, ct);

        return new DataTablesResponse<TResult>
        {
            Draw = req.Draw,
            RecordsTotal = recordsTotal,
            RecordsFiltered = recordsFiltered,
            Data = data
        };
    }

    // ── Provider dispatch ────────────────────────────────────────────────────
    // MongoDB.Driver 3.x doesn't expose IMongoQueryable<T> publicly — both EF
    // and Mongo return plain IQueryable<T>. Detect by inspecting the provider
    // namespace and call the matching async extension.

    private static bool IsMongo<T>(IQueryable<T> q)
        => q.Provider.GetType().FullName?.StartsWith("MongoDB.", StringComparison.Ordinal) == true;

    private static Task<int> CountAsync<T>(IQueryable<T> q, CancellationToken ct)
        => IsMongo(q)
            ? MongoQueryable.CountAsync(q, ct)
            : EntityFrameworkQueryableExtensions.CountAsync(q, ct);

    private static async Task<List<T>> ToListAsync<T>(IQueryable<T> q, CancellationToken ct)
        => IsMongo(q)
            ? await MongoQueryable.ToListAsync(q, ct)
            : await EntityFrameworkQueryableExtensions.ToListAsync(q, ct);
}
