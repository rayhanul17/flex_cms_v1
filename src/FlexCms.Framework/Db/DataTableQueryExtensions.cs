using System.Linq.Expressions;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Models;
using Microsoft.EntityFrameworkCore;

namespace FlexCms.Framework.Db;

/// <summary>
/// One-call helper that turns a <see cref="DataTablesRequest"/> + a base
/// <see cref="IQueryable{T}"/> into a paginated, sorted, searched
/// <see cref="DataTablesResponse{T}"/>. Intended to be called from
/// <c>BaseAdminController.DataTableResult</c>.
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
        var recordsTotal = await source.CountAsync(ct);

        var filtered = source;
        if (!string.IsNullOrWhiteSpace(req.SearchValue) && globalSearchFilter is not null)
            filtered = filtered.Where(globalSearchFilter);

        var recordsFiltered = await filtered.CountAsync(ct);

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

        var data = await page.ToListAsync(ct);

        return new DataTablesResponse<TResult>
        {
            Draw = req.Draw,
            RecordsTotal = recordsTotal,
            RecordsFiltered = recordsFiltered,
            Data = data
        };
    }
}
