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

    // Site info
    public string SiteName { get; set; } = "FlexCms";
    public string SiteTagline { get; set; } = "";
    public string SiteBaseUrl { get; set; } = "";
    public string DefaultLanguage { get; set; } = "en";

    // Admin account — AdminPasswordEncrypted cleared after first seed
    public string AdminEmail { get; set; } = string.Empty;
    public string AdminFullName { get; set; } = "";
    public string AdminPasswordEncrypted { get; set; } = string.Empty;
    public bool AdminSeeded { get; set; }

    public string SetupVersion { get; set; } = "1.0";
    public DateTime SetupCompletedAt { get; set; }
    public string TimeZoneId { get; set; } = "Asia/Dhaka";
}
