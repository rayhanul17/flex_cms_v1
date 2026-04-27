namespace FlexCms.Framework.Setup;

public class SetupConfig
{
    public bool IsSetupComplete { get; set; }
    public string DbProvider { get; set; } = "mysql";
    public string DbConnectionString { get; set; } = string.Empty;

    // Password stored encrypted via DataProtection
    public string DbPasswordEncrypted { get; set; } = string.Empty;

    public string MongoConnectionString { get; set; } = string.Empty;
    public string MongoDatabase { get; set; } = "flexcms";
    public string SiteName { get; set; } = "FlexCms";
    public string AdminEmail { get; set; } = string.Empty;
    public string SetupVersion { get; set; } = "1.0";
    public DateTime SetupCompletedAt { get; set; }
}
