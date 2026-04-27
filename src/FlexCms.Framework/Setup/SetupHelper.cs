using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace FlexCms.Framework.Setup;

public class SetupHelper
{
    private const string Purpose = "FlexCms.Setup.DbPassword";
    private readonly IDataProtector _protector;
    private readonly string _setupFilePath;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SetupHelper(IDataProtectionProvider dataProtection, string appDataPath)
    {
        _protector = dataProtection.CreateProtector(Purpose);
        _setupFilePath = Path.Combine(appDataPath, "setup.json");
    }

    public bool IsSetupComplete()
    {
        if (!File.Exists(_setupFilePath)) return false;
        var config = Read();
        return config?.IsSetupComplete == true;
    }

    public SetupConfig? Read()
    {
        if (!File.Exists(_setupFilePath)) return null;
        var json = File.ReadAllText(_setupFilePath);
        return JsonSerializer.Deserialize<SetupConfig>(json, JsonOpts);
    }

    public void Write(SetupConfig config)
    {
        var dir = Path.GetDirectoryName(_setupFilePath)!;
        Directory.CreateDirectory(dir);

        // Encrypt password before persisting if provided in plaintext
        if (!string.IsNullOrEmpty(config.DbPasswordEncrypted) &&
            !config.DbPasswordEncrypted.StartsWith("CfDJ8", StringComparison.Ordinal))
        {
            config.DbPasswordEncrypted = _protector.Protect(config.DbPasswordEncrypted);
        }

        var json = JsonSerializer.Serialize(config, JsonOpts);
        File.WriteAllText(_setupFilePath, json);
    }

    public string DecryptPassword(string encrypted)
        => _protector.Unprotect(encrypted);
}
