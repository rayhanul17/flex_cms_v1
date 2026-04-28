using System.ComponentModel.DataAnnotations;

namespace FlexCms.Host.Models.Setup;

public class SetupStep1ViewModel
{
    [Required]
    public string DbProvider { get; set; } = "mysql";

    // MySQL fields
    public string? MySqlHost { get; set; } = "localhost";
    public int MySqlPort { get; set; } = 3306;
    public string? MySqlDatabase { get; set; } = "flexcms";
    public string? MySqlUsername { get; set; } = "root";
    public string? MySqlPassword { get; set; }

    // MongoDB fields
    public string? MongoConnectionString { get; set; } = "mongodb://localhost:27017";
    public string? MongoDatabase { get; set; } = "flexcms";
}
