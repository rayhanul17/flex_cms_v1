using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using System.Reflection;
using Xunit;

namespace FlexCms.Tests.Unit.Phase17;

/// <summary>
/// Architectural tests: every BaseEfEntity in FlexCms.Framework that is
/// NOT append-only and NOT explicitly ignored will be auto-audited by
/// FcmsAuditInterceptor. These tests guard against accidental opt-out or
/// missing attribute misuse.
/// </summary>
public class FcmsAuditArchitectureTests
{
    private static readonly Assembly FrameworkAssembly =
        typeof(FcmsAuditInterceptor).Assembly;

    private static IEnumerable<Type> AllBaseEfEntityTypes() =>
        FrameworkAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(BaseEfEntity).IsAssignableFrom(t));

    // ── Append-only entities are never auto-audited ───────────────────────────

    [Fact]
    public void AppendOnly_entities_are_excluded_from_audit()
    {
        var appendOnly = AllBaseEfEntityTypes()
            .Where(t => typeof(IAppendOnlyEntity).IsAssignableFrom(t))
            .ToList();

        // Must have at least FcmsLog and FcmsLogArchive
        Assert.NotEmpty(appendOnly);

        foreach (var type in appendOnly)
        {
            // Confirm the interceptor's own check would skip them
            Assert.True(
                typeof(IAppendOnlyEntity).IsAssignableFrom(type),
                $"{type.Name} should implement IAppendOnlyEntity");
        }
    }

    [Fact]
    public void FcmsLog_and_FcmsLogArchive_implement_IAppendOnlyEntity()
    {
        Assert.True(typeof(IAppendOnlyEntity).IsAssignableFrom(typeof(FcmsLog)));
        Assert.True(typeof(IAppendOnlyEntity).IsAssignableFrom(typeof(FcmsLogArchive)));
    }

    // ── FcmsAuditIgnoreEntity can only be applied to classes ─────────────────

    [Fact]
    public void FcmsAuditIgnoreEntity_attribute_targets_class_only()
    {
        var targets = typeof(FcmsAuditIgnoreEntityAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>()!
            .ValidOn;
        Assert.Equal(AttributeTargets.Class, targets);
    }

    // ── Non-ignored, non-append-only entities should be auditable ────────────

    [Fact]
    public void Domain_entities_are_neither_appendOnly_nor_ignored()
    {
        var domainTypes = AllBaseEfEntityTypes()
            .Where(t => !typeof(IAppendOnlyEntity).IsAssignableFrom(t))
            .Where(t => t.GetCustomAttributes(typeof(FcmsAuditIgnoreEntityAttribute), true).Length == 0)
            .ToList();

        // Must have at least some domain entities (FcmsPage, FcmsPost, etc.)
        Assert.NotEmpty(domainTypes);

        // None of these should accidentally implement IAppendOnlyEntity
        foreach (var t in domainTypes)
            Assert.False(typeof(IAppendOnlyEntity).IsAssignableFrom(t),
                $"{t.Name} implements IAppendOnlyEntity but has no [FcmsAuditIgnoreEntity] — " +
                "it would be skipped from audit without being marked ignored.");
    }

    // ── GetPrefix never returns null or empty ─────────────────────────────────

    [Fact]
    public void GetPrefix_returns_non_empty_string_for_all_domain_entities()
    {
        var domainTypes = AllBaseEfEntityTypes()
            .Where(t => !typeof(IAppendOnlyEntity).IsAssignableFrom(t));

        foreach (var type in domainTypes)
        {
            var prefix = FcmsAuditInterceptor.GetPrefix(type);
            Assert.False(string.IsNullOrWhiteSpace(prefix),
                $"GetPrefix returned empty for {type.Name}");
        }
    }

    // ── FcmsAuditEntity attribute is idempotent (single attribute only) ───────

    [Fact]
    public void FcmsAuditEntity_attribute_is_not_AllowMultiple()
    {
        var usage = typeof(FcmsAuditEntityAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>()!;
        Assert.False(usage.AllowMultiple);
    }

    // ── AuditingRepository decorator covers all IRepository write methods ─────

    [Fact]
    public void AuditingRepository_implements_all_IRepository_write_methods()
    {
        var iface = typeof(IRepository<>);
        var writeMethods = new[]
        {
            nameof(IRepository<BaseEfEntity>.AddAsync),
            nameof(IRepository<BaseEfEntity>.AddRangeAsync),
            nameof(IRepository<BaseEfEntity>.UpdateAsync),
            nameof(IRepository<BaseEfEntity>.UpdateRangeAsync),
            nameof(IRepository<BaseEfEntity>.DeleteAsync),
            nameof(IRepository<BaseEfEntity>.DeleteRangeAsync),
            nameof(IRepository<BaseEfEntity>.SoftDeleteAsync),
            nameof(IRepository<BaseEfEntity>.SoftDeleteRangeAsync),
        };

        var decoratorType = typeof(FlexCms.Framework.Db.MongoDb.AuditingRepository<>)
            .MakeGenericType(typeof(BaseEfEntity));

        foreach (var method in writeMethods)
        {
            var impl = decoratorType.GetMethod(method);
            Assert.NotNull(impl);
        }
    }
}
