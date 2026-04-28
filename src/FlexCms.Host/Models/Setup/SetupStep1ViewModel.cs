using System.ComponentModel.DataAnnotations;

namespace FlexCms.Host.Models.Setup;

public class SetupStep1ViewModel
{
    [Required]
    public string DbProvider { get; set; } = "mysql";

    // ── MySQL ──────────────────────────────────────────────────────────────────
    public string? MySqlHost { get; set; } = "localhost";
    public int MySqlPort { get; set; } = 3306;
    public string? MySqlDatabase { get; set; } = "flexcms";
    public string? MySqlUsername { get; set; } = "root";
    public string? MySqlPassword { get; set; }

    // ── MSSQL (SQL Server) ────────────────────────────────────────────────────
    public string? MsSqlHost { get; set; } = "localhost";
    public int MsSqlPort { get; set; } = 1433;
    public string? MsSqlDatabase { get; set; } = "flexcms";
    public string? MsSqlUsername { get; set; } = "sa";
    public string? MsSqlPassword { get; set; }

    // ── PostgreSQL ────────────────────────────────────────────────────────────
    public string? PgHost { get; set; } = "localhost";
    public int PgPort { get; set; } = 5432;
    public string? PgDatabase { get; set; } = "flexcms";
    public string? PgUsername { get; set; } = "postgres";
    public string? PgPassword { get; set; }

    // ── MongoDB ────────────────────────────────────────────────────────────────
    public string? MongoConnectionString { get; set; } = "mongodb://localhost:27017";
    public string? MongoDatabase { get; set; } = "flexcms";
}
