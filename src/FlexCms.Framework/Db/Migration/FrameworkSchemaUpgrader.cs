using FlexCms.Framework.Db.Ef;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Db.Migration;

/// <summary>
/// Idempotent startup schema upgrader.
///
/// <para>
/// The framework's initial schema is created via <c>EnsureCreatedAsync()</c>
/// during the setup wizard. That is fine on a fresh install but it does NOT
/// re-apply changes when columns are added later — so every time the
/// framework adds a column to a long-lived entity we ship a defensive
/// <c>ALTER TABLE ... ADD COLUMN IF NOT EXISTS</c> here.
/// </para>
///
/// <para>
/// The statements are written to be safe on MySQL 8+, PostgreSQL 9.6+, and
/// SQL Server 2016+. They are no-ops when the column already exists, so
/// running this on every startup costs at most a metadata round-trip per
/// expected column.
/// </para>
/// </summary>
public sealed class FrameworkSchemaUpgrader : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<FrameworkSchemaUpgrader> _logger;

    public FrameworkSchemaUpgrader(IServiceScopeFactory scopes, ILogger<FrameworkSchemaUpgrader> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetService<FcmsDbContext>();
        if (ctx is null) return; // no relational DB configured

        var provider = ctx.Database.ProviderName ?? "";

        try
        {
            await RunForProviderAsync(ctx, provider, ct);
            _logger.LogInformation("Framework schema upgrades applied.");
        }
        catch (Exception ex)
        {
            // Log + swallow — the home page must still load even if a single
            // ALTER fails; the admin will see specific failures on first use.
            _logger.LogError(ex, "FrameworkSchemaUpgrader: failed to apply one or more upgrades.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private static async Task RunForProviderAsync(FcmsDbContext ctx, string provider, CancellationToken ct)
    {
        // Each tuple = (table, column, type-specific SQL fragment).
        var upgrades = new (string Table, string Column, string ColumnDdl)[]
        {
            ("fcms_module_records", "LastActivationAttemptAt", "datetime(6) NULL"),
            ("fcms_module_records", "ActivationError",         "varchar(2000) NULL"),
        };

        foreach (var (table, column, ddl) in upgrades)
        {
            // Existence check first — we emulate "ADD COLUMN IF NOT EXISTS"
            // across providers rather than relying on each dialect's syntax,
            // because MySQL 8 still doesn't accept IF NOT EXISTS on ADD COLUMN.
            if (await ColumnExistsAsync(ctx, table, column, ct)) continue;

            var sql = BuildAddColumnSql(provider, table, column, ddl);
            if (sql is null) continue;
            await ctx.Database.ExecuteSqlRawAsync(sql, ct);
        }
    }

    /// <summary>
    /// Cross-provider "does this column exist?" check using
    /// <c>INFORMATION_SCHEMA.COLUMNS</c>, which MySQL, PostgreSQL, and SQL
    /// Server all expose with the same shape.
    /// </summary>
    private static async Task<bool> ColumnExistsAsync(FcmsDbContext ctx, string table, string column, CancellationToken ct)
    {
        var conn = ctx.Database.GetDbConnection();
        var opened = false;
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
            opened = true;
        }
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = @t AND COLUMN_NAME = @c;";
            var pT = cmd.CreateParameter(); pT.ParameterName = "@t"; pT.Value = table; cmd.Parameters.Add(pT);
            var pC = cmd.CreateParameter(); pC.ParameterName = "@c"; pC.Value = column; cmd.Parameters.Add(pC);
            var result = await cmd.ExecuteScalarAsync(ct);
            return Convert.ToInt32(result) > 0;
        }
        finally
        {
            if (opened) await conn.CloseAsync();
        }
    }

    /// <summary>
    /// Returns a provider-specific ALTER TABLE statement (or null when the
    /// provider isn't recognised). The caller has already verified the
    /// column does not exist, so this is plain ALTER TABLE — no IF NOT EXISTS
    /// clause needed (MySQL 8 still rejects that keyword on ADD COLUMN).
    /// </summary>
    private static string? BuildAddColumnSql(string provider, string table, string column, string columnDdl)
    {
        if (provider.Contains("MySql", StringComparison.OrdinalIgnoreCase) ||
            provider.Contains("Pomelo", StringComparison.OrdinalIgnoreCase))
        {
            return $"ALTER TABLE `{table}` ADD COLUMN `{column}` {columnDdl};";
        }

        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            provider.Contains("Postgres", StringComparison.OrdinalIgnoreCase))
        {
            var pgType = columnDdl
                .Replace("datetime(6)", "timestamp", StringComparison.OrdinalIgnoreCase);
            return $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {pgType};";
        }

        if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            var mssqlType = columnDdl
                .Replace("datetime(6)", "datetime2", StringComparison.OrdinalIgnoreCase)
                .Replace("varchar", "nvarchar", StringComparison.OrdinalIgnoreCase);
            return $"ALTER TABLE [{table}] ADD [{column}] {mssqlType};";
        }

        return null;
    }
}
