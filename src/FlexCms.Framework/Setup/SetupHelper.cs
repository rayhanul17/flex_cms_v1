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

    // Static check — no DI required; used in Program.cs before container is built
    public static bool IsSetupComplete(string appDataPath)
    {
        var path = Path.Combine(appDataPath, "setup.json");
        if (!File.Exists(path)) return false;
        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("isSetupComplete", out var el) && el.GetBoolean();
        }
        catch { return false; }
    }

    // Static read — no DI required; used in Program.cs to build FlexCmsOptions from setup.json
    public static SetupConfig? ReadStatic(string appDataPath)
    {
        var path = Path.Combine(appDataPath, "setup.json");
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SetupConfig>(json, JsonOpts);
        }
        catch { return null; }
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

        if (!string.IsNullOrEmpty(config.DbPasswordEncrypted) &&
            !config.DbPasswordEncrypted.StartsWith("CfDJ8", StringComparison.Ordinal))
            config.DbPasswordEncrypted = _protector.Protect(config.DbPasswordEncrypted);

        if (!string.IsNullOrEmpty(config.AdminPasswordEncrypted) &&
            !config.AdminPasswordEncrypted.StartsWith("CfDJ8", StringComparison.Ordinal))
            config.AdminPasswordEncrypted = _protector.Protect(config.AdminPasswordEncrypted);

        var json = JsonSerializer.Serialize(config, JsonOpts);
        File.WriteAllText(_setupFilePath, json);
    }

    public string EncryptValue(string plaintext) => _protector.Protect(plaintext);
    public string DecryptPassword(string encrypted) => _protector.Unprotect(encrypted);
}
