using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using FlexCms.Framework.Clock;
using FlexCms.Framework.Db;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Backup;

/// <summary>
/// Mongo equivalent of <see cref="FcmsBackupService"/>. Same ZIP layout
/// (<c>_metadata.json</c> + <c>entities/{Collection}.json</c> + media +
/// config) so the admin UI / restore form is interchangeable across
/// backends. Each collection is dumped as a JSON array of documents
/// (BSON → MongoDB extended JSON via <see cref="JsonWriterSettings"/>),
/// preserving ObjectId / Date types for round-trip restore.
///
/// <para>
/// Identical retention + path-traversal guards as the EF impl. Audit-log
/// collections are excluded from the dump for the same reason — append-
/// only, large, no value in restoring stale history.
/// </para>
/// </summary>
public sealed class MongoBackupService : IFcmsBackupService
{
    private const string MetadataEntryName = "_metadata.json";
    private const string EntityFolderPrefix = "entities/";
    private const string MediaFolderPrefix = "media/";
    private const string ConfigFolderPrefix = "config/";

    /// <summary>Collections we never round-trip — append-only audit logs.</summary>
    private static readonly HashSet<string> ExcludedCollections = new(StringComparer.OrdinalIgnoreCase)
    {
        "fcms_logs",
        "fcms_log_archives",
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    private static readonly JsonWriterSettings BsonJsonSettings = new()
    {
        OutputMode = JsonOutputMode.CanonicalExtendedJson,
    };

    private readonly IMongoDatabase _db;
    private readonly IHostEnvironment _env;
    private readonly ILogger<MongoBackupService> _logger;

    public MongoBackupService(IMongoDatabase db, IHostEnvironment env, ILogger<MongoBackupService> logger)
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
            // 1. Collections — every collection in the DB except the excluded ones.
            // Mongo's ListCollectionNames is paged; the cursor hands us strings
            // we filter client-side (the small handful of names is cheap).
            var cursor = await _db.ListCollectionNamesAsync(cancellationToken: ct);
            var names = await cursor.ToListAsync(ct);
            foreach (var name in names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                if (ExcludedCollections.Contains(name)) continue;
                var coll = _db.GetCollection<BsonDocument>(name);
                var docs = await coll.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync(ct);

                // Render as a JSON array of canonical-extended-JSON documents.
                // Canonical mode preserves type info ({"$oid":..., "$date":...})
                // so the restore can rebuild ObjectId / DateTime exactly.
                var array = "[" + string.Join(",", docs.Select(d => d.ToJson(BsonJsonSettings))) + "]";
                var entry = zip.CreateEntry($"{EntityFolderPrefix}{name}.json", CompressionLevel.Optimal);
                await using var es = entry.Open();
                await using var sw = new StreamWriter(es);
                await sw.WriteAsync(array.AsMemory(), ct);
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
                        if (file.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) continue;
                        var rel = Path.GetRelativePath(mediaRoot, file).Replace('\\', '/');
                        zip.CreateEntryFromFile(file, $"{MediaFolderPrefix}{rel}", CompressionLevel.Optimal);
                    }
                }
            }

            // 3. Config (optional)
            if (options.IncludeConfig)
            {
                var setupJson = Path.Combine(_env.ContentRootPath, "App_Data", "setup.json");
                if (File.Exists(setupJson))
                    zip.CreateEntryFromFile(setupJson, $"{ConfigFolderPrefix}setup.json");
            }

            // 4. Metadata — humans + restore tool both read this. Backend tag
            // lets the restore code refuse a wrong-backend ZIP up front.
            var meta = new
            {
                createdAt = ts,
                framework = "FlexCms",
                backend = "mongo",
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
        _logger.LogInformation("Mongo backup created: {File} ({Size} bytes, {Count} collections)",
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

            // 1. Restore collections. Per-entry: drop + reseed via InsertMany
            // (idempotent re-create even if backup contains _id values that
            // already exist in the target — DropCollection wipes first).
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(EntityFolderPrefix)))
            {
                var collName = Path.GetFileNameWithoutExtension(entry.FullName);
                if (string.IsNullOrEmpty(collName) || ExcludedCollections.Contains(collName)) continue;

                await using var es = entry.Open();
                using var reader = new StreamReader(es);
                var json = await reader.ReadToEndAsync(ct);
                if (string.IsNullOrWhiteSpace(json) || json.Trim() == "[]") continue;

                // BsonSerializer.Deserialize handles canonical-extended-JSON
                // out of the box so {"$oid"}, {"$date"} round-trip back to
                // ObjectId / DateTime.
                var arr = BsonSerializer.Deserialize<BsonArray>(json);
                var docs = arr.OfType<BsonDocument>().ToList();
                if (docs.Count == 0) continue;

                await _db.DropCollectionAsync(collName, ct);
                var coll = _db.GetCollection<BsonDocument>(collName);
                await coll.InsertManyAsync(docs, cancellationToken: ct);
                entityCount++;
            }

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

            _logger.LogInformation("Mongo restore complete from {File}: {Entities} collections, {Media} media files",
                fileName, entityCount, mediaCount);
            return new RestoreResult(true, entityCount, mediaCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mongo restore failed for {File}", fileName);
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
}
