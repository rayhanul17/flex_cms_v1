using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Modules;

/// <summary>
/// Reads <c>fcms_module_records.PackageHashSha256</c> via raw ADO.NET.
/// Used by <see cref="ModuleManager.ScanAndLoad"/> to check module
/// integrity BEFORE <c>Assembly.LoadFrom</c> runs — at that point EF/DI
/// isn't built yet, so we can't use the normal repository. See
/// security-audit-recheck-report §8.1.
///
/// <para>
/// On the very first boot of a fresh install the schema upgrader may
/// not have run yet (the column doesn't exist) — the store reports
/// unavailable in that case and module load falls back to trust-on-
/// first-use. The activator then records the hash for next boot.
/// </para>
/// </summary>
public sealed class AdoModuleTrustStore : IModuleTrustStore
{
    private readonly Dictionary<string, string> _approved;
    public bool IsAvailable { get; }

    private AdoModuleTrustStore(Dictionary<string, string> approved, bool available)
    {
        _approved = approved;
        IsAvailable = available;
    }

    public string? GetApprovedHash(string moduleId)
        => _approved.TryGetValue(moduleId, out var h) ? h : null;

    /// <summary>
    /// Eagerly snapshot the table once at scan time. The map is then
    /// queried in-process per module — module discovery hits it dozens of
    /// times, no point making a round-trip each time.
    /// </summary>
    public static IModuleTrustStore Build(
        string provider,
        string connectionString,
        ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(provider))
            return NullModuleTrustStore.Instance;

        try
        {
            using var conn = OpenConnection(provider, connectionString);
            if (conn is null) return NullModuleTrustStore.Instance;

            // Quote the column + table the way each provider does.
            // Postgres requires double-quotes preserving case; MySQL uses
            // backticks; SQL Server uses brackets. Our schema is the same
            // shape across all three.
            string sql = provider switch
            {
                _ when provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
                       provider.Contains("Postgres", StringComparison.OrdinalIgnoreCase) ||
                       provider.Equals("postgresql", StringComparison.OrdinalIgnoreCase)
                       => @"SELECT ""ModuleId"", ""PackageHashSha256"" FROM fcms_module_records WHERE ""PackageHashSha256"" IS NOT NULL",

                _ when provider.Contains("MySql", StringComparison.OrdinalIgnoreCase) ||
                       provider.Contains("Pomelo", StringComparison.OrdinalIgnoreCase) ||
                       provider.Equals("mysql", StringComparison.OrdinalIgnoreCase)
                       => "SELECT `ModuleId`, `PackageHashSha256` FROM `fcms_module_records` WHERE `PackageHashSha256` IS NOT NULL",

                _ when provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
                       provider.Equals("mssql", StringComparison.OrdinalIgnoreCase)
                       => "SELECT [ModuleId], [PackageHashSha256] FROM [fcms_module_records] WHERE [PackageHashSha256] IS NOT NULL",

                _ => "",
            };

            if (string.IsNullOrEmpty(sql)) return NullModuleTrustStore.Instance;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandType = CommandType.Text;

            var approved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(1)) continue;
                var id = reader.GetString(0);
                var hash = reader.GetString(1);
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(hash))
                    approved[id] = hash;
            }
            return new AdoModuleTrustStore(approved, available: true);
        }
        catch (Exception ex)
        {
            // Table missing on first boot, network blip, etc. — degrade
            // gracefully so a fresh install can still load modules.
            logger?.LogWarning(ex,
                "ModuleTrustStore: could not load approved hashes (provider={Provider}). " +
                "First-boot trust-on-first-use applies; integrity will start enforcing once " +
                "the activator records hashes.", provider);
            return NullModuleTrustStore.Instance;
        }
    }

    private static DbConnection? OpenConnection(string provider, string connectionString)
    {
        // We deliberately resolve providers by string match instead of
        // adding hard NuGet deps here — Framework already references the
        // provider DLLs transitively for EF, and using ADO.NET this way
        // avoids dragging EF into the pre-DI bootstrap path.
        DbProviderFactory? factory = null;
        if (provider.Equals("postgresql", StringComparison.OrdinalIgnoreCase) ||
            provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            factory = TryGetFactory("Npgsql.NpgsqlFactory, Npgsql");
        else if (provider.Equals("mysql", StringComparison.OrdinalIgnoreCase) ||
                 provider.Contains("Pomelo", StringComparison.OrdinalIgnoreCase) ||
                 provider.Contains("MySql", StringComparison.OrdinalIgnoreCase))
            factory = TryGetFactory("MySqlConnector.MySqlConnectorFactory, MySqlConnector");
        else if (provider.Equals("mssql", StringComparison.OrdinalIgnoreCase) ||
                 provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            factory = TryGetFactory("Microsoft.Data.SqlClient.SqlClientFactory, Microsoft.Data.SqlClient");

        if (factory is null) return null;
        var conn = factory.CreateConnection();
        if (conn is null) return null;
        conn.ConnectionString = connectionString;
        conn.Open();
        return conn;
    }

    private static DbProviderFactory? TryGetFactory(string typeName)
    {
        try
        {
            var t = Type.GetType(typeName);
            if (t is null) return null;
            var instance = t.GetField("Instance",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null);
            return instance as DbProviderFactory;
        }
        catch { return null; }
    }
}
