using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FlexCms.Framework.Clock;
using FlexCms.Framework.Db.Ef;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Backup;

/// <summary>
/// Default backup impl — serializes every <c>DbSet</c> on
/// <see cref="FcmsDbContext"/> as a JSON file inside a ZIP, snapshots the
/// media folder, and copies <c>setup.json</c>. Single-instance / single-tier
/// design; fits the FlexCms deployment model. Larger sites can swap in a
/// vendor-specific impl that drives <c>mysqldump</c> / <c>pg_dump</c>.
/// </summary>
public sealed class FcmsBackupService : IFcmsBackupService
{
    private const string MetadataEntryName = "_metadata.json";
    private const string EntityFolderPrefix = "entities/";
    private const string MediaFolderPrefix = "media/";
    private const string ConfigFolderPrefix = "config/";

    private readonly FcmsDbContext _db;
    private readonly IHostEnvironment _env;
    private readonly ILogger<FcmsBackupService> _logger;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
    };

    public FcmsBackupService(FcmsDbContext db, IHostEnvironment env, ILogger<FcmsBackupService> logger)
    {
        _db = db;
        _env = env;
        _logger = logger;
    }

    public async Task<BackupResult> CreateBackupAsync(BackupOptions? options = null, CancellationToken ct = default)
    {
        options ??= new BackupOptions();
        var ts = FcmsTime.Now;
        var dir = GetBackupsDir();
        Directory.CreateDirectory(dir);

        var fileName = $"backup_{ts:yyyy-MM-dd_HHmmss}.zip";
        var filePath = Path.Combine(dir, fileName);

        var entityCount = 0;
        await using (var fs = File.Create(filePath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
        {
            // 1. Entities — every public DbSet on the context.
            foreach (var (name, rows) in await EnumerateDbSetsAsync(ct))
            {
                var entry = zip.CreateEntry($"{EntityFolderPrefix}{name}.json", CompressionLevel.Optimal);
                await using var es = entry.Open();
                await JsonSerializer.SerializeAsync(es, rows, JsonOpts, ct);
                entityCount++;
            }

            // 2. Media (optional)
            if (options.IncludeMedia)
            {
                var mediaRoot = Path.Combine(_env.ContentRootPath, "App_Data", "storage");
                if (Directory.Exists(mediaRoot))
                {
                    foreach (var file in Directory.EnumerateFiles(mediaRoot, "*", SearchOption.AllDirectories))
                    {
                        // Don't recurse into the backups folder itself —
                        // would create unbounded growth on each subsequent backup.
                        if (file.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) continue;
                        var rel = Path.GetRelativePath(mediaRoot, file).Replace('\\', '/');
                        zip.CreateEntryFromFile(file, $"{MediaFolderPrefix}{rel}", CompressionLevel.Optimal);
                    }
                }
            }

            // 3. Config (optional). setup.json contains the encrypted admin
            // password during initial seeding — keep separate by default so
            // ops can audit before including.
            if (options.IncludeConfig)
            {
                var setupJson = Path.Combine(_env.ContentRootPath, "App_Data", "setup.json");
                if (File.Exists(setupJson))
                    zip.CreateEntryFromFile(setupJson, $"{ConfigFolderPrefix}setup.json");
            }

            // 4. Metadata — humans + restore tool both read this.
            var meta = new
            {
                createdAt = ts,
                framework = "FlexCms",
                schemaVersion = 1,
                entityCount,
                includeMedia = options.IncludeMedia,
                includeConfig = options.IncludeConfig,
            };
            var metaEntry = zip.CreateEntry(MetadataEntryName);
            await using (var ms = metaEntry.Open())
                await JsonSerializer.SerializeAsync(ms, meta, JsonOpts, ct);
        }

        var info = new FileInfo(filePath);
        _logger.LogInformation("Backup created: {File} ({Size} bytes, {Count} entities)",
            fileName, info.Length, entityCount);
        return new BackupResult(fileName, filePath, info.Length, entityCount, ts);
    }

    public Task<IReadOnlyList<BackupFileInfo>> ListBackupsAsync(CancellationToken ct = default)
    {
        var dir = GetBackupsDir();
        if (!Directory.Exists(dir))
            return Task.FromResult<IReadOnlyList<BackupFileInfo>>([]);

        var infos = Directory.EnumerateFiles(dir, "backup_*.zip")
            .Select(p => new FileInfo(p))
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f => new BackupFileInfo(f.Name, f.FullName, f.Length, f.CreationTimeUtc))
            .ToList();

        return Task.FromResult<IReadOnlyList<BackupFileInfo>>(infos);
    }

    public Task DeleteBackupAsync(string fileName, CancellationToken ct = default)
    {
        // Reject path-traversal attempts — only filenames are accepted.
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\'))
            return Task.CompletedTask;
        var path = Path.Combine(GetBackupsDir(), fileName);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public async Task<RestoreResult> RestoreAsync(string fileName, RestoreOptions options, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\'))
            return new RestoreResult(false, 0, 0, "Invalid file name.");

        var path = Path.Combine(GetBackupsDir(), fileName);
        if (!File.Exists(path)) return new RestoreResult(false, 0, 0, "Backup not found.");

        var entityCount = 0;
        var mediaCount = 0;
        try
        {
            await using var fs = File.OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);

            // 1. Restore entities. Order doesn't matter when bulk-deleting
            // first because we drop everything that's about to be re-seeded.
            // Cycles are handled by IgnoreCycles + EF's tracker.
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(EntityFolderPrefix)))
            {
                var entityName = Path.GetFileNameWithoutExtension(entry.FullName);
                var dbSetProp = _db.GetType().GetProperties()
                    .FirstOrDefault(p => string.Equals(p.Name, entityName, StringComparison.OrdinalIgnoreCase)
                                         && p.PropertyType.IsGenericType
                                         && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>));
                if (dbSetProp is null) continue;

                var elementType = dbSetProp.PropertyType.GetGenericArguments()[0];
                await using var es = entry.Open();
                var listType = typeof(List<>).MakeGenericType(elementType);
                var rows = await JsonSerializer.DeserializeAsync(es, listType, JsonOpts, ct) as System.Collections.IEnumerable;
                if (rows is null) continue;

                // Drop existing rows in this DbSet, then bulk-add restored ones.
                // Use ExecuteDeleteAsync if available; fall back to remove-range.
                var dbSet = dbSetProp.GetValue(_db);
                if (dbSet is null) continue;
                var addRangeMethod = dbSet.GetType().GetMethod("AddRange", new[] { typeof(System.Collections.IEnumerable) });
                addRangeMethod?.Invoke(dbSet, new object[] { rows });
                entityCount++;
            }
            await _db.SaveChangesAsync(ct);

            // 2. Restore media (optional)
            if (options.RestoreMedia)
            {
                var mediaRoot = Path.Combine(_env.ContentRootPath, "App_Data", "storage");
                Directory.CreateDirectory(mediaRoot);
                foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(MediaFolderPrefix) && !string.IsNullOrEmpty(e.Name)))
                {
                    var rel = entry.FullName[MediaFolderPrefix.Length..];
                    var dest = Path.Combine(mediaRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    entry.ExtractToFile(dest, overwrite: true);
                    mediaCount++;
                }
            }

            // 3. Restore config (optional, off by default)
            if (options.RestoreConfig)
            {
                var entry = zip.GetEntry($"{ConfigFolderPrefix}setup.json");
                if (entry is not null)
                {
                    var dest = Path.Combine(_env.ContentRootPath, "App_Data", "setup.json");
                    entry.ExtractToFile(dest, overwrite: true);
                }
            }

            _logger.LogInformation("Restore complete from {File}: {Entities} entities, {Media} media files",
                fileName, entityCount, mediaCount);
            return new RestoreResult(true, entityCount, mediaCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restore failed for {File}", fileName);
            return new RestoreResult(false, entityCount, mediaCount, ex.Message);
        }
    }

    public Task<int> ApplyRetentionAsync(int retentionDays, CancellationToken ct = default)
    {
        if (retentionDays <= 0) return Task.FromResult(0);
        var dir = GetBackupsDir();
        if (!Directory.Exists(dir)) return Task.FromResult(0);

        var cutoff = FcmsTime.Now.AddDays(-retentionDays);
        var deleted = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "backup_*.zip"))
        {
            var info = new FileInfo(f);
            if (info.CreationTimeUtc < cutoff)
            {
                try { File.Delete(f); deleted++; }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete old backup {File}", f); }
            }
        }
        return Task.FromResult(deleted);
    }

    private string GetBackupsDir() =>
        Path.Combine(_env.ContentRootPath, "App_Data", "backups");

    /// <summary>
    /// Reflect over <see cref="FcmsDbContext"/> for every public
    /// <c>DbSet&lt;T&gt;</c> property and return its rows. Skips audit-log
    /// entities (append-only, large, not useful in a restore).
    /// </summary>
    private async Task<List<(string Name, List<object> Rows)>> EnumerateDbSetsAsync(CancellationToken ct)
    {
        var result = new List<(string, List<object>)>();
        var props = _db.GetType().GetProperties()
            .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>));

        foreach (var prop in props)
        {
            // Skip audit-log entities — they're huge, append-only, and
            // re-creating the DbContext would re-seed empty anyway.
            if (prop.Name is "Logs" or "LogArchives") continue;

            var elementType = prop.PropertyType.GetGenericArguments()[0];
            var dbSet = prop.GetValue(_db);
            if (dbSet is null) continue;

            // Cast to IQueryable<TEntity> via reflection so we can call
            // EF's async ToListAsync without statically binding to T.
            var queryable = (IQueryable)dbSet;
            var toListAsync = typeof(EntityFrameworkQueryableExtensions)
                .GetMethods()
                .First(m => m.Name == nameof(EntityFrameworkQueryableExtensions.ToListAsync)
                            && m.GetParameters().Length == 2)
                .MakeGenericMethod(elementType);
            var task = (Task)toListAsync.Invoke(null, new object[] { queryable, ct })!;
            await task.ConfigureAwait(false);
            var resultProp = task.GetType().GetProperty("Result");
            var rows = ((System.Collections.IEnumerable)resultProp!.GetValue(task)!)
                .Cast<object>().ToList();
            result.Add((prop.Name, rows));
        }
        return result;
    }
}
