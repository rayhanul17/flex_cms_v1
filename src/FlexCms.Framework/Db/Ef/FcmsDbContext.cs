using FlexCms.Framework.Api;
using FlexCms.Framework.Auth;
using FlexCms.Framework.Auth.History;
using FlexCms.Framework.Chat;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Cms.Comments;
using FlexCms.Framework.Cms.CustomFields;
using FlexCms.Framework.Cms.Revisions;
using FlexCms.Framework.Clock;
using FlexCms.Framework.FeatureFlags;
using FlexCms.Framework.Seo;
using FlexCms.Framework.Db;
using FlexCms.Framework.Exports;
using FlexCms.Framework.Helpers;
using FlexCms.Framework.Messaging;
using FlexCms.Framework.Modules;
using FlexCms.Framework.Newsletters;
using FlexCms.Framework.Notifications;
using FlexCms.Framework.Sessions;
using FlexCms.Framework.Webhooks;
using FlexCms.Framework.Widgets;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FlexCms.Framework.Db.Ef;

public class FcmsDbContext : IdentityDbContext<FcmsUser, FcmsRole, Guid>
{
    /// <summary>Framework table prefix — applied to every Core/Identity table.</summary>
    public const string FrameworkPrefix = "fcms";

    private readonly IEnumerable<IFcmsModelBuilder> _moduleBuilders;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    /// <summary>Used by tests — no module builders, no HttpContext.</summary>
    public FcmsDbContext(DbContextOptions<FcmsDbContext> options)
        : this(options, [], null) { }

    /// <summary>Used by DI — module builders injected for OnModelCreating.</summary>
    public FcmsDbContext(DbContextOptions<FcmsDbContext> options, IEnumerable<IFcmsModelBuilder> moduleBuilders,
        IHttpContextAccessor? httpContextAccessor = null)
        : base(options)
    {
        _moduleBuilders = moduleBuilders;
        _httpContextAccessor = httpContextAccessor;
    }

    private Guid? CurrentUserId()
    {
        var claim = _httpContextAccessor?.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    public DbSet<FcmsSettings> Settings => Set<FcmsSettings>();
    public DbSet<FcmsPermission> Permissions => Set<FcmsPermission>();
    public DbSet<FcmsRolePermission> RolePermissions => Set<FcmsRolePermission>();
    public DbSet<FcmsModuleRecord> ModuleRecords => Set<FcmsModuleRecord>();
    public DbSet<FcmsMenuItem> MenuItems => Set<FcmsMenuItem>();

    // CMS
    public DbSet<FcmsPage> Pages => Set<FcmsPage>();
    public DbSet<FcmsCategory> Categories => Set<FcmsCategory>();
    public DbSet<FcmsPost> Posts => Set<FcmsPost>();
    public DbSet<FcmsTag> Tags => Set<FcmsTag>();
    public DbSet<FcmsPostTag> PostTags => Set<FcmsPostTag>();
    public DbSet<FcmsRedirect> Redirects => Set<FcmsRedirect>();
    public DbSet<FcmsMediaFolder> MediaFolders => Set<FcmsMediaFolder>();
    public DbSet<FcmsMedia> Media => Set<FcmsMedia>();
    public DbSet<FcmsPageTranslation> PageTranslations => Set<FcmsPageTranslation>();
    public DbSet<FcmsPostTranslation> PostTranslations => Set<FcmsPostTranslation>();

    // Audit logs
    public DbSet<FcmsLog> Logs => Set<FcmsLog>();
    public DbSet<FcmsLogArchive> LogArchives => Set<FcmsLogArchive>();

    // Phase 8 — restart-safe message queue
    public DbSet<FcmsPendingMessage> PendingMessages => Set<FcmsPendingMessage>();

    // Phase 9 — notifications + widgets
    public DbSet<FcmsNotification> Notifications => Set<FcmsNotification>();
    public DbSet<FcmsWidgetPlacement> WidgetPlacements => Set<FcmsWidgetPlacement>();

    // Phase 10 — chat
    public DbSet<FcmsChatThread> ChatThreads => Set<FcmsChatThread>();
    public DbSet<FcmsChatMessage> ChatMessages => Set<FcmsChatMessage>();

    // Phase 12 — async exports
    public DbSet<FcmsPendingExport> PendingExports => Set<FcmsPendingExport>();

    // Phase 13 — auth hardening
    public DbSet<FcmsUserSession> UserSessions => Set<FcmsUserSession>();
    public DbSet<FcmsLoginHistory> LoginHistory => Set<FcmsLoginHistory>();

    // Phase 14 — API tokens / webhooks / engagement
    public DbSet<FcmsApiToken> ApiTokens => Set<FcmsApiToken>();
    public DbSet<FcmsWebhookEndpoint> WebhookEndpoints => Set<FcmsWebhookEndpoint>();
    public DbSet<FcmsWebhookDelivery> WebhookDeliveries => Set<FcmsWebhookDelivery>();
    public DbSet<FcmsContentRevision> ContentRevisions => Set<FcmsContentRevision>();
    public DbSet<FcmsComment> Comments => Set<FcmsComment>();
    public DbSet<FcmsSubscriber> Subscribers => Set<FcmsSubscriber>();
    public DbSet<FcmsContentMeta> ContentMeta => Set<FcmsContentMeta>();

    // ── Phase 15: SEO + Feature flags ─────────────────────────────────────
    public DbSet<FcmsSeoMeta> SeoMeta => Set<FcmsSeoMeta>();
    public DbSet<FcmsFeatureFlag> FeatureFlags => Set<FcmsFeatureFlag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Identity tables — generic type names don't snake_case nicely, so set
        // them explicitly. Convention: fcms_ + plural_snake_case_noun.
        modelBuilder.Entity<FcmsUser>().ToTable("fcms_users");
        modelBuilder.Entity<FcmsRole>().ToTable("fcms_roles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("fcms_user_roles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("fcms_user_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("fcms_user_logins");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("fcms_role_claims");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("fcms_user_tokens");

        // Embedded collections used in Mongo only; ignore in EF
        modelBuilder.Entity<FcmsUser>().Ignore(u => u.Roles);
        modelBuilder.Entity<FcmsUser>().Ignore(u => u.Claims);
        modelBuilder.Entity<FcmsUser>().Ignore(u => u.Logins);
        modelBuilder.Entity<FcmsUser>().Ignore(u => u.Tokens);

        modelBuilder.Entity<FcmsRole>().Ignore(r => r.Claims);

        // Unique index: one permission key per role
        modelBuilder.Entity<FcmsRolePermission>()
            .HasIndex(rp => new { rp.RoleId, rp.PermissionKey })
            .IsUnique();

        // FK: deleting a role hard-removes its permission rows. Soft-delete
        // (Status=Deleted) is a column update and does NOT trigger this cascade.
        modelBuilder.Entity<FcmsRolePermission>()
            .HasOne<FcmsRole>()
            .WithMany()
            .HasForeignKey(rp => rp.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        // Unique index: permission key must be unique globally
        modelBuilder.Entity<FcmsPermission>()
            .HasIndex(p => p.Key)
            .IsUnique();

        // Unique index: one record per module ID
        modelBuilder.Entity<FcmsModuleRecord>()
            .HasIndex(m => m.ModuleId)
            .IsUnique();

        // ── CMS ────────────────────────────────────────────────────────────────

        // Pages: self-referential hierarchy; restrict cascade to avoid cycles
        modelBuilder.Entity<FcmsPage>()
            .HasOne(p => p.Parent)
            .WithMany(p => p.Children)
            .HasForeignKey(p => p.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<FcmsPage>()
            .HasIndex(p => p.Slug)
            .IsUnique();

        // Categories: self-referential hierarchy
        modelBuilder.Entity<FcmsCategory>()
            .HasOne(c => c.Parent)
            .WithMany(c => c.Children)
            .HasForeignKey(c => c.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<FcmsCategory>()
            .HasIndex(c => c.Slug)
            .IsUnique();

        // Posts
        modelBuilder.Entity<FcmsPost>()
            .HasOne(p => p.Category)
            .WithMany(c => c.Posts)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<FcmsPost>()
            .HasIndex(p => p.Slug)
            .IsUnique();

        // Tags
        modelBuilder.Entity<FcmsTag>()
            .HasIndex(t => t.Slug)
            .IsUnique();

        // PostTags: junction table with unique index
        modelBuilder.Entity<FcmsPostTag>()
            .HasIndex(pt => new { pt.PostId, pt.TagId })
            .IsUnique();

        modelBuilder.Entity<FcmsPostTag>()
            .ToTable("fcms_post_tags");

        modelBuilder.Entity<FcmsPostTag>()
            .HasOne(pt => pt.Post)
            .WithMany(p => p.PostTags)
            .HasForeignKey(pt => pt.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<FcmsPostTag>()
            .HasOne(pt => pt.Tag)
            .WithMany(t => t.PostTags)
            .HasForeignKey(pt => pt.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        // Redirects: unique FromPath
        modelBuilder.Entity<FcmsRedirect>()
            .HasIndex(r => r.FromPath)
            .IsUnique();

        // MediaFolders: self-referential hierarchy
        modelBuilder.Entity<FcmsMediaFolder>()
            .HasOne(f => f.Parent)
            .WithMany(f => f.Children)
            .HasForeignKey(f => f.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Media: FK to folder (nullable — root-level media has no folder)
        modelBuilder.Entity<FcmsMedia>()
            .HasOne(m => m.Folder)
            .WithMany(f => f.Media)
            .HasForeignKey(m => m.FolderId)
            .OnDelete(DeleteBehavior.SetNull);

        // ── Translations (Phase 7) ────────────────────────────────────────────

        modelBuilder.Entity<FcmsPageTranslation>()
            .HasOne(t => t.Page)
            .WithMany(p => p.Translations)
            .HasForeignKey(t => t.PageId)
            .OnDelete(DeleteBehavior.Cascade);

        // (PageId, LanguageCode) unique → at most one translation per language
        modelBuilder.Entity<FcmsPageTranslation>()
            .HasIndex(t => new { t.PageId, t.LanguageCode })
            .IsUnique();

        // (LanguageCode, Slug) unique → /bn/about-us and /en/about-us coexist
        modelBuilder.Entity<FcmsPageTranslation>()
            .HasIndex(t => new { t.LanguageCode, t.Slug })
            .IsUnique();

        modelBuilder.Entity<FcmsPostTranslation>()
            .HasOne(t => t.Post)
            .WithMany(p => p.Translations)
            .HasForeignKey(t => t.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<FcmsPostTranslation>()
            .HasIndex(t => new { t.PostId, t.LanguageCode })
            .IsUnique();

        modelBuilder.Entity<FcmsPostTranslation>()
            .HasIndex(t => new { t.LanguageCode, t.Slug })
            .IsUnique();

        // ── Pending message queue (Phase 8) ───────────────────────────────────
        // Index supports the Pending|Failed-with-retries-left scan that
        // MessageProcessorService runs every 30 seconds.
        modelBuilder.Entity<FcmsPendingMessage>()
            .HasIndex(m => new { m.DeliveryStatus, m.RetryCount });

        modelBuilder.Entity<FcmsPendingMessage>()
            .HasIndex(m => m.BroadcastId);

        // ── Notifications + widgets (Phase 9) ─────────────────────────────────

        // Bell-icon "unread for me" query → composite index.
        modelBuilder.Entity<FcmsNotification>()
            .HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAt });

        // Zone render lookup hit on every page → covering index.
        modelBuilder.Entity<FcmsWidgetPlacement>()
            .HasIndex(p => new { p.Zone, p.Enabled, p.SortOrder });

        // ── Chat (Phase 10) ────────────────────────────────────────────────────

        // Thread → messages cascade so resolving / hard-deleting a thread cleans up its messages.
        modelBuilder.Entity<FcmsChatMessage>()
            .HasOne(m => m.Thread)
            .WithMany(t => t.Messages)
            .HasForeignKey(m => m.ThreadId)
            .OnDelete(DeleteBehavior.Cascade);

        // (UserId, ThreadStatus) — fast lookup of "open thread for user".
        modelBuilder.Entity<FcmsChatThread>()
            .HasIndex(t => new { t.UserId, t.ThreadStatus });

        // Admin list ordering query.
        modelBuilder.Entity<FcmsChatThread>()
            .HasIndex(t => t.LastMessageAt);

        // (ThreadId, CreatedAt) — message timeline render.
        modelBuilder.Entity<FcmsChatMessage>()
            .HasIndex(m => new { m.ThreadId, m.CreatedAt });

        // ── Exports (Phase 12) ─────────────────────────────────────────────────
        // Processor scan: WHERE export_status = Pending ORDER BY created_at.
        modelBuilder.Entity<FcmsPendingExport>()
            .HasIndex(e => new { e.ExportStatus, e.CreatedAt });

        // ── Auth hardening (Phase 13) ─────────────────────────────────────────

        // (UserId, IsRevoked) — "active sessions for me" admin/profile lookup.
        modelBuilder.Entity<FcmsUserSession>()
            .HasIndex(s => new { s.UserId, s.IsRevoked });

        // SessionId — used by the per-request validation middleware.
        modelBuilder.Entity<FcmsUserSession>()
            .HasIndex(s => s.SessionId)
            .IsUnique();

        // Login history is append-only — Index by (Outcome, CreatedAt) for the
        // failed-attempt reports the security dashboard runs.
        modelBuilder.Entity<FcmsLoginHistory>()
            .HasIndex(h => new { h.Outcome, h.CreatedAt });

        modelBuilder.Entity<FcmsLoginHistory>()
            .HasIndex(h => h.AttemptedUserName);

        // ── Engagement / API (Phase 14) ───────────────────────────────────────

        modelBuilder.Entity<FcmsApiToken>().HasIndex(t => t.Hash).IsUnique();
        modelBuilder.Entity<FcmsApiToken>().HasIndex(t => t.UserId);

        modelBuilder.Entity<FcmsWebhookEndpoint>().HasIndex(e => e.IsActive);

        // Failed-but-retriable scan: WHERE delivery_status=Pending AND attempt_count<3
        modelBuilder.Entity<FcmsWebhookDelivery>()
            .HasIndex(d => new { d.DeliveryStatus, d.AttemptCount });

        modelBuilder.Entity<FcmsContentRevision>()
            .HasIndex(r => new { r.EntityType, r.EntityId, r.Version });

        modelBuilder.Entity<FcmsComment>()
            .HasIndex(c => new { c.EntityType, c.EntityId, c.CommentStatus });
        modelBuilder.Entity<FcmsComment>().HasIndex(c => c.ParentId);

        modelBuilder.Entity<FcmsSubscriber>().HasIndex(s => s.Email).IsUnique();
        modelBuilder.Entity<FcmsSubscriber>().HasIndex(s => s.Token).IsUnique();
        modelBuilder.Entity<FcmsSubscriber>().HasIndex(s => s.SubscriberStatus);

        modelBuilder.Entity<FcmsContentMeta>()
            .HasIndex(m => new { m.EntityType, m.EntityId, m.Key })
            .IsUnique();

        // ── Phase 15: SEO + Feature flags ─────────────────────────────────
        // (EntityType, EntityId) is the natural key — at most one SEO row per entity.
        modelBuilder.Entity<FcmsSeoMeta>()
            .HasIndex(s => new { s.EntityType, s.EntityId })
            .IsUnique();

        modelBuilder.Entity<FcmsFeatureFlag>()
            .HasIndex(f => f.Key)
            .IsUnique();

        // Audit log entities are append-only — strip the inherited lifecycle
        // columns and skip the soft-delete query filter (no Status column means
        // the filter expression would target a missing column).
        ConfigureLogEntity<FcmsLog>(modelBuilder);
        ConfigureLogEntity<FcmsLogArchive>(modelBuilder);

        // Module builders — each registered IFcmsModelBuilder configures its
        // own entities (tables, indexes, FKs) into this shared DbContext.
        foreach (var builder in _moduleBuilders)
            builder.Build(modelBuilder);

        // Apply soft-delete filter + auto-name table for every BaseEfEntity
        // EXCEPT the log entities (already configured above).
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(BaseEfEntity).IsAssignableFrom(entityType.ClrType)) continue;
            if (entityType.ClrType == typeof(FcmsLog) || entityType.ClrType == typeof(FcmsLogArchive)) continue;

            var method = typeof(FcmsDbContext)
                .GetMethod(nameof(ApplySoftDeleteFilter),
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(entityType.ClrType);

            method.Invoke(null, [modelBuilder]);
        }
    }

    private static void ApplySoftDeleteFilter<T>(ModelBuilder builder) where T : BaseEfEntity
    {
        builder.Entity<T>().HasQueryFilter(e => e.Status != EntityStatus.Deleted);
        builder.Entity<T>().ToTable(FcmsHelper.GetTableName<T>(FrameworkPrefix));
    }

    private static void ConfigureLogEntity<T>(ModelBuilder builder) where T : BaseEfEntity
    {
        var entity = builder.Entity<T>();
        // Strip inherited lifecycle columns — logs are write-once
        entity.Ignore(e => e.Status);
        entity.Ignore(e => e.DeletedAt);
        entity.Ignore(e => e.UpdatedAt);
        entity.Ignore(e => e.UpdatedBy);
        entity.Ignore(e => e.CreatedBy);  // UserId field carries the actor
        entity.ToTable(FcmsHelper.GetTableName<T>(FrameworkPrefix));
        // No HasQueryFilter — logs are visible regardless of any "deleted" semantics
    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var userId = CurrentUserId();
        var now = FcmsTime.Now;

        foreach (var entry in ChangeTracker.Entries<BaseEfEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedBy ??= userId;
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedBy = userId;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedBy = userId;
            }
        }
        return await base.SaveChangesAsync(ct);
    }
}
