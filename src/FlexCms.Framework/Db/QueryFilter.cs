using System.Linq.Expressions;

namespace FlexCms.Framework.Db;

public class QueryFilter<T> where T : class, IBaseEntity
{
    // internal state — read by repository implementations
    internal List<Expression<Func<T, bool>>> Conditions { get; } = new();
    internal Expression<Func<T, object>>? OrderByExpr { get; private set; }
    internal bool IsDescending { get; private set; }
    internal int? PageNumber { get; private set; }
    internal int? PageSize { get; private set; }

    public QueryFilter<T> Where(Expression<Func<T, bool>> predicate)
    { Conditions.Add(predicate); return this; }

    public QueryFilter<T> OrderBy(Expression<Func<T, object>> keySelector)
    { OrderByExpr = keySelector; IsDescending = false; return this; }

    public QueryFilter<T> OrderByDescending(Expression<Func<T, object>> keySelector)
    { OrderByExpr = keySelector; IsDescending = true; return this; }

    public QueryFilter<T> Page(int page, int pageSize)
    { PageNumber = page; PageSize = pageSize; return this; }

    internal bool IsPaged => PageNumber.HasValue && PageSize.HasValue;
}
