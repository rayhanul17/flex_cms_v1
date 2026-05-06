using FlexCms.Framework.Auth;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Helpers;
using FlexCms.Framework.Modules;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace FlexCms.Framework.Db.MongoDb;

/// <summary>
/// Creates MongoDB indexes at startup that mirror the unique constraints and
/// compound indexes defined in <c>FcmsDbContext.OnModelCreating</c>.
/// Idempotent — MongoDB silently skips existing indexes with the same name.
/// </summary>
public class MongoIndexService : IHostedService
{
    private readonly IMongoDatabase _db;
    private readonly ILogger<MongoIndexService> _logger;

    public MongoIndexService(IMongoDatabase db, ILogger<MongoIndexService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        try { await CreateIndexesAsync(ct); }
        catch (Exception ex) { _logger.LogError(ex, "MongoIndexService: failed to create indexes."); }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private async Task CreateIndexesAsync(CancellationToken ct)
    {
        // ── Identity ──────────────────────────────────────────────────────────

        // FcmsUser.NormalizedUserName unique (mirrors EF Identity convention)
        await UniqueAsync<FcmsUser>(
            Builders<FcmsUser>.IndexKeys.Ascending(u => u.NormalizedUserName),
            "ux_users_normalized_user_name", ct);

        // FcmsUser.NormalizedEmail unique
        await UniqueAsync<FcmsUser>(
            Builders<FcmsUser>.IndexKeys.Ascending(u => u.NormalizedEmail),
            "ux_users_normalized_email", ct);

        // FcmsRole.NormalizedName unique
        await UniqueAsync<FcmsRole>(
            Builders<FcmsRole>.IndexKeys.Ascending(r => r.NormalizedName),
            "ux_roles_normalized_name", ct);

        // ── Auth ──────────────────────────────────────────────────────────────

        // FcmsPermission.Key unique
        await UniqueAsync<FcmsPermission>(
            Builders<FcmsPermission>.IndexKeys.Ascending(p => p.Key),
            "ux_permissions_key", ct);

        // FcmsRolePermission (RoleId, PermissionKey) unique composite
        await UniqueAsync<FcmsRolePermission>(
            Builders<FcmsRolePermission>.IndexKeys
                .Ascending(rp => rp.RoleId)
                .Ascending(rp => rp.PermissionKey),
            "ux_role_permissions_role_key", ct);

        // RoleId index for fast lookup when revoking/querying by role
        await IndexAsync<FcmsRolePermission>(
            Builders<FcmsRolePermission>.IndexKeys.Ascending(rp => rp.RoleId),
            "ix_role_permissions_role_id", ct);

        // ── CMS ───────────────────────────────────────────────────────────────

        // FcmsPage.Slug unique
        await UniqueAsync<FcmsPage>(
            Builders<FcmsPage>.IndexKeys.Ascending(p => p.Slug),
            "ux_pages_slug", ct);

        // FcmsCategory.Slug unique
        await UniqueAsync<FcmsCategory>(
            Builders<FcmsCategory>.IndexKeys.Ascending(c => c.Slug),
            "ux_categories_slug", ct);

        // FcmsPost.Slug unique
        await UniqueAsync<FcmsPost>(
            Builders<FcmsPost>.IndexKeys.Ascending(p => p.Slug),
            "ux_posts_slug", ct);

        // FcmsTag.Slug unique
        await UniqueAsync<FcmsTag>(
            Builders<FcmsTag>.IndexKeys.Ascending(t => t.Slug),
            "ux_tags_slug", ct);

        // FcmsPostTag (PostId, TagId) unique composite
        await UniqueAsync<FcmsPostTag>(
            Builders<FcmsPostTag>.IndexKeys
                .Ascending(pt => pt.PostId)
                .Ascending(pt => pt.TagId),
            "ux_post_tags_post_tag", ct);

        // FcmsRedirect.FromPath unique
        await UniqueAsync<FcmsRedirect>(
            Builders<FcmsRedirect>.IndexKeys.Ascending(r => r.FromPath),
            "ux_redirects_from_path", ct);

        // ── Translations (Phase 7) ────────────────────────────────────────────

        // (PageId, LanguageCode) unique
        await UniqueAsync<FcmsPageTranslation>(
            Builders<FcmsPageTranslation>.IndexKeys
                .Ascending(t => t.PageId)
                .Ascending(t => t.LanguageCode),
            "ux_page_translations_page_lang", ct);

        // (LanguageCode, Slug) unique
        await UniqueAsync<FcmsPageTranslation>(
            Builders<FcmsPageTranslation>.IndexKeys
                .Ascending(t => t.LanguageCode)
                .Ascending(t => t.Slug),
            "ux_page_translations_lang_slug", ct);

        // (PostId, LanguageCode) unique
        await UniqueAsync<FcmsPostTranslation>(
            Builders<FcmsPostTranslation>.IndexKeys
                .Ascending(t => t.PostId)
                .Ascending(t => t.LanguageCode),
            "ux_post_translations_post_lang", ct);

        // (LanguageCode, Slug) unique
        await UniqueAsync<FcmsPostTranslation>(
            Builders<FcmsPostTranslation>.IndexKeys
                .Ascending(t => t.LanguageCode)
                .Ascending(t => t.Slug),
            "ux_post_translations_lang_slug", ct);

        // ── Modules ───────────────────────────────────────────────────────────

        // FcmsModuleRecord.ModuleId unique
        await UniqueAsync<FcmsModuleRecord>(
            Builders<FcmsModuleRecord>.IndexKeys.Ascending(m => m.ModuleId),
            "ux_module_records_module_id", ct);

        // ── Settings ─────────────────────────────────────────────────────────

        // FcmsSettings.Key unique
        await UniqueAsync<Db.FcmsSettings>(
            Builders<Db.FcmsSettings>.IndexKeys.Ascending(s => s.Key),
            "ux_settings_key", ct);

        _logger.LogInformation("MongoIndexService: indexes ensured.");
    }

    private Task UniqueAsync<T>(IndexKeysDefinition<T> keys, string name, CancellationToken ct)
        => GetCollection<T>().Indexes.CreateOneAsync(
            new CreateIndexModel<T>(keys, new CreateIndexOptions { Unique = true, Name = name, Background = true }),
            cancellationToken: ct);

    private Task IndexAsync<T>(IndexKeysDefinition<T> keys, string name, CancellationToken ct)
        => GetCollection<T>().Indexes.CreateOneAsync(
            new CreateIndexModel<T>(keys, new CreateIndexOptions { Name = name, Background = true }),
            cancellationToken: ct);

    private IMongoCollection<T> GetCollection<T>()
        => _db.GetCollection<T>(FcmsHelper.GetTableName<T>("fcms"));
}
