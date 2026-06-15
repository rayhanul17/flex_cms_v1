using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace FlexCms.Framework.Db;

/// <summary>
/// Lightweight raw-SQL helper that returns DTOs by reflection — no Dapper, no
/// extra NuGet. Use for reports and aggregates where the EF model doesn't
/// match the projection shape (e.g. <c>GROUP BY</c> rollups, cross-table
/// summaries).
///
/// <para>
/// Column-to-property mapping is case-insensitive, matches by name first and
/// optional <see cref="FcmsSqlColumnAttribute"/> override second. <c>DBNull</c>
/// becomes <c>null</c> (or the type default for non-nullable value types).
/// </para>
///
/// <example>
/// <code>
/// var summary = await FcmsSqlHelper.QueryAsync&lt;InvestmentSummaryDto&gt;(
///     ctx,
///     "SELECT InvestorEmail, COUNT(*) AS InvestmentCount, SUM(Amount) AS TotalAmount " +
///     "FROM investments GROUP BY InvestorEmail",
///     ct: ct);
/// </code>
/// </example>
/// </summary>
public static class FcmsSqlHelper
{
    /// <summary>
    /// Execute <paramref name="sql"/> against <paramref name="ctx"/>'s
    /// connection and materialize each result row as <typeparamref name="TDto"/>.
    /// Parameters bind by name (use <c>@p</c> placeholders) or by index
    /// (positional — <c>{0}</c>, <c>{1}</c> in the SQL with positional
    /// parameter objects in <paramref name="parameters"/>).
    /// </summary>
    public static async Task<IReadOnlyList<TDto>> QueryAsync<TDto>(
        DbContext ctx,
        string sql,
        IReadOnlyList<KeyValuePair<string, object?>>? parameters = null,
        CancellationToken ct = default)
        where TDto : class, new()
    {
        var conn = ctx.Database.GetDbConnection();
        var opened = false;
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
            opened = true;
        }
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            if (parameters is not null)
            {
                foreach (var (name, value) in parameters)
                {
                    var p = cmd.CreateParameter();
                    p.ParameterName = name;
                    p.Value = value ?? DBNull.Value;
                    cmd.Parameters.Add(p);
                }
            }

            // Apply EF's command timeout so callers don't get an unexpectedly
            // different cancellation horizon than the rest of the DbContext.
            var efTimeout = ctx.Database.GetCommandTimeout();
            if (efTimeout.HasValue) cmd.CommandTimeout = efTimeout.Value;

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return await ReadAllAsync<TDto>(reader, ct);
        }
        finally
        {
            if (opened) await conn.CloseAsync();
        }
    }

    /// <summary>
    /// Convenience overload that returns a single scalar — uses
    /// <see cref="DbCommand.ExecuteScalarAsync"/> directly.
    /// </summary>
    public static async Task<T?> ScalarAsync<T>(
        DbContext ctx,
        string sql,
        IReadOnlyList<KeyValuePair<string, object?>>? parameters = null,
        CancellationToken ct = default)
    {
        var conn = ctx.Database.GetDbConnection();
        var opened = false;
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
            opened = true;
        }
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            if (parameters is not null)
            {
                foreach (var (name, value) in parameters)
                {
                    var p = cmd.CreateParameter();
                    p.ParameterName = name;
                    p.Value = value ?? DBNull.Value;
                    cmd.Parameters.Add(p);
                }
            }
            var efTimeout = ctx.Database.GetCommandTimeout();
            if (efTimeout.HasValue) cmd.CommandTimeout = efTimeout.Value;

            var raw = await cmd.ExecuteScalarAsync(ct);
            if (raw is null or DBNull) return default;
            var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            try { return (T)Convert.ChangeType(raw, target, CultureInfo.InvariantCulture); }
            catch { return default; }
        }
        finally
        {
            if (opened) await conn.CloseAsync();
        }
    }

    /// <summary>
    /// Convenience overload that runs a non-query SQL (INSERT/UPDATE/DELETE/DDL)
    /// and returns the affected-row count.
    /// </summary>
    public static async Task<int> ExecuteAsync(
        DbContext ctx,
        string sql,
        IReadOnlyList<KeyValuePair<string, object?>>? parameters = null,
        CancellationToken ct = default)
    {
        var conn = ctx.Database.GetDbConnection();
        var opened = false;
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
            opened = true;
        }
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            if (parameters is not null)
            {
                foreach (var (name, value) in parameters)
                {
                    var p = cmd.CreateParameter();
                    p.ParameterName = name;
                    p.Value = value ?? DBNull.Value;
                    cmd.Parameters.Add(p);
                }
            }
            var efTimeout = ctx.Database.GetCommandTimeout();
            if (efTimeout.HasValue) cmd.CommandTimeout = efTimeout.Value;
            return await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (opened) await conn.CloseAsync();
        }
    }

    // ── internals ─────────────────────────────────────────────────────────

    private static async Task<IReadOnlyList<TDto>> ReadAllAsync<TDto>(DbDataReader reader, CancellationToken ct)
        where TDto : class, new()
    {
        var result = new List<TDto>();
        if (!reader.HasRows) return result;

        // Build a column-index → property map once.
        var props = typeof(TDto).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .ToList();

        var nameToProp = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in props)
        {
            var key = p.GetCustomAttribute<FcmsSqlColumnAttribute>()?.ColumnName ?? p.Name;
            nameToProp[key] = p;
        }

        var indexToProp = new PropertyInfo?[reader.FieldCount];
        for (int i = 0; i < reader.FieldCount; i++)
        {
            var col = reader.GetName(i);
            indexToProp[i] = nameToProp.TryGetValue(col, out var p) ? p : null;
        }

        while (await reader.ReadAsync(ct))
        {
            var dto = new TDto();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var prop = indexToProp[i];
                if (prop is null) continue;
                if (reader.IsDBNull(i)) continue;

                var raw = reader.GetValue(i);
                var converted = ChangeType(raw, prop.PropertyType);
                if (converted is not null) prop.SetValue(dto, converted);
            }
            result.Add(dto);
        }

        return result;
    }

    private static object? ChangeType(object raw, Type targetType)
    {
        var target = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (target.IsInstanceOfType(raw)) return raw;
        if (target.IsEnum)
        {
            if (raw is string s && Enum.TryParse(target, s, ignoreCase: true, out var ev)) return ev;
            try { return Enum.ToObject(target, raw); } catch { return null; }
        }
        try { return Convert.ChangeType(raw, target, CultureInfo.InvariantCulture); }
        catch { return null; }
    }
}

/// <summary>
/// Optional column-name override for <see cref="FcmsSqlHelper"/> mapping. Use
/// when the SELECT alias doesn't match the DTO property name.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class FcmsSqlColumnAttribute : Attribute
{
    public string ColumnName { get; }
    public FcmsSqlColumnAttribute(string columnName) => ColumnName = columnName;
}
