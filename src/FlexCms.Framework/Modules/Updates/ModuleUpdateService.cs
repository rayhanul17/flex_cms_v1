using System.IO.Compression;
using System.Text.Json;
using FlexCms.Framework.Db;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Modules.Updates;

public sealed class ModuleUpdateService : IModuleUpdateService
{
    private readonly IHostEnvironment _env;
    private readonly IRepository<FcmsModuleRecord> _records;
    private readonly IFcmsUnitOfWork _uow;
    private readonly ILogger<ModuleUpdateService> _logger;

    public ModuleUpdateService(
        IHostEnvironment env,
        IRepository<FcmsModuleRecord> records,
        IFcmsUnitOfWork uow,
        ILogger<ModuleUpdateService> logger)
    {
        _env = env;
        _records = records;
        _uow = uow;
        _logger = logger;
    }

    public async Task<ModuleUpdateResult> UpdateAsync(string moduleId, string newPackagePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
            return new ModuleUpdateResult(false, null, null, "Module id required.");
        if (string.IsNullOrWhiteSpace(newPackagePath) || !File.Exists(newPackagePath) && !Directory.Exists(newPackagePath))
            return new ModuleUpdateResult(false, null, null, "Package path does not exist.");

        var modulesRoot = Path.Combine(_env.ContentRootPath, "modules");
        var moduleDir = Path.Combine(modulesRoot, moduleId);
        if (!Directory.Exists(moduleDir))
            return new ModuleUpdateResult(false, null, null, $"Module folder '{moduleId}' does not exist.");

        // 1. Snapshot current state for rollback.
        var record = (await _records.FindAsync(r => r.ModuleId == moduleId, ct)).FirstOrDefault();
        var fromVersion = record?.Version;
        var backupDir = Path.Combine(modulesRoot, $".{moduleId}.backup_{DateTime.UtcNow:yyyyMMddHHmmss}");

        try
        {
            // Backup existing folder by renaming — atomic on the same volume,
            // and avoids byte-by-byte copy time.
            Directory.Move(moduleDir, backupDir);
            Directory.CreateDirectory(moduleDir);

            // 2. Lay out the new package into the module folder.
            if (Directory.Exists(newPackagePath))
            {
                CopyDirectory(newPackagePath, moduleDir);
            }
            else
            {
                // Treat as ZIP.
                ZipFile.ExtractToDirectory(newPackagePath, moduleDir, overwriteFiles: true);
            }

            // 3. Read new manifest to learn the target version.
            var newVersion = TryReadManifestVersion(moduleDir) ?? "unknown";

            // 4. Update the DB record. NOTE: actual module migrations run
            // when the host restarts and the new binaries activate — we
            // only flip the record here so the post-restart loader picks
            // up the new version. If the new binaries fail to load on
            // restart, the operator can call this method again with the
            // backup path to roll back.
            if (record is not null)
            {
                record.Version = newVersion;
                record.UpdatedAt = DateTime.UtcNow;
                await _records.UpdateAsync(record, ct);
                await _uow.SaveChangesAsync(ct);
            }

            _logger.LogInformation("Module {Module} updated from {From} → {To}. Backup at {Backup}.",
                moduleId, fromVersion, newVersion, backupDir);
            return new ModuleUpdateResult(true, fromVersion, newVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Module {Module} update failed. Attempting rollback.", moduleId);

            // Rollback: drop the partial new folder + restore the backup.
            try
            {
                if (Directory.Exists(moduleDir)) Directory.Delete(moduleDir, recursive: true);
                if (Directory.Exists(backupDir)) Directory.Move(backupDir, moduleDir);
                return new ModuleUpdateResult(false, fromVersion, null, ex.Message, RolledBack: true);
            }
            catch (Exception rbEx)
            {
                _logger.LogError(rbEx, "Module {Module} rollback failed. Manual intervention required: backup at {Backup}.", moduleId, backupDir);
                return new ModuleUpdateResult(false, fromVersion, null, $"{ex.Message}; rollback also failed: {rbEx.Message}", RolledBack: false);
            }
        }
    }

    private static void CopyDirectory(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(src, dest));
        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            File.Copy(f, f.Replace(src, dest), overwrite: true);
    }

    private static string? TryReadManifestVersion(string moduleDir)
    {
        var manifestPath = Path.Combine(moduleDir, "module.json");
        if (!File.Exists(manifestPath)) return null;
        try
        {
            using var fs = File.OpenRead(manifestPath);
            var doc = JsonDocument.Parse(fs);
            return doc.RootElement.TryGetProperty("Version", out var v) ? v.GetString()
                 : doc.RootElement.TryGetProperty("version", out var v2) ? v2.GetString()
                 : null;
        }
        catch { return null; }
    }
}
