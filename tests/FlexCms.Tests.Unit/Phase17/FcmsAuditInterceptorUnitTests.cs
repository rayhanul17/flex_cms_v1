using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlexCms.Tests.Unit.Phase17;

/// <summary>
/// Pure unit tests for FcmsAuditInterceptor static helpers.
/// No database required — exercises action derivation, prefix logic,
/// and entity opt-out rules via InternalsVisibleTo.
///
/// DeriveAction tests use FcmsPage (registered in FcmsDbContext model)
/// so the InMemory change-tracker accepts them.
/// </summary>
public class FcmsAuditInterceptorUnitTests
{
    // ── Fixtures for GetPrefix / attribute tests only ─────────────────────────
    // These types are NOT added to a DbContext — only their Type metadata is used.

    private sealed class FcmsWidget : BaseEfEntity { }       // Fcms-prefix stripping
    private sealed class PlainEntity : BaseEfEntity { }      // no Fcms prefix

    [FcmsAuditEntity("BlogPost")]
    private sealed class FcmsAnnotatedEntity : BaseEfEntity { }

    [FcmsAuditIgnoreEntity]
    private sealed class FcmsIgnoredEntity : BaseEfEntity { }

    private sealed class AppendOnlyStub : BaseEfEntity, IAppendOnlyEntity { }

    // ── GetPrefix ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetPrefix_strips_Fcms_prefix_from_type_name()
    {
        Assert.Equal("Widget", FcmsAuditInterceptor.GetPrefix(typeof(FcmsWidget)));
        // Real framework types
        Assert.Equal("Page", FcmsAuditInterceptor.GetPrefix(typeof(FcmsPage)));
        Assert.Equal("Post", FcmsAuditInterceptor.GetPrefix(typeof(FcmsPost)));
    }

    [Fact]
    public void GetPrefix_leaves_non_Fcms_names_unchanged()
    {
        Assert.Equal("PlainEntity", FcmsAuditInterceptor.GetPrefix(typeof(PlainEntity)));
    }

    [Fact]
    public void GetPrefix_uses_FcmsAuditEntity_attribute_when_present()
    {
        Assert.Equal("BlogPost", FcmsAuditInterceptor.GetPrefix(typeof(FcmsAnnotatedEntity)));
    }

    // ── DeriveAction (via EF InMemory change tracker with real DbContext types) ──

    [Fact]
    public void DeriveAction_Added_returns_Created_Info()
    {
        using var db = BuildDb();
        db.Set<FcmsPage>().Add(new FcmsPage { Title = "T", Slug = "s" });

        var entry = db.ChangeTracker.Entries<BaseEfEntity>().First();
        var (verb, severity) = FcmsAuditInterceptor.DeriveAction(entry);

        Assert.Equal("Created", verb);
        Assert.Equal(FcmsLogSeverity.Info, severity);
    }

    [Fact]
    public void DeriveAction_Modified_returns_Updated_Info()
    {
        using var db = BuildDb();
        var entity = new FcmsPage { Id = Guid.NewGuid(), Title = "T", Slug = "s" };
        db.Set<FcmsPage>().Attach(entity);
        entity.Title = "Changed";
        db.Entry(entity).State = EntityState.Modified;

        var entry = db.ChangeTracker.Entries<BaseEfEntity>().First();
        var (verb, severity) = FcmsAuditInterceptor.DeriveAction(entry);

        Assert.Equal("Updated", verb);
        Assert.Equal(FcmsLogSeverity.Info, severity);
    }

    [Fact]
    public void DeriveAction_Modified_with_Status_Deleted_returns_Deleted_Info()
    {
        using var db = BuildDb();
        var entity = new FcmsPage { Id = Guid.NewGuid(), Title = "T", Slug = "s", Status = EntityStatus.Deleted };
        db.Set<FcmsPage>().Attach(entity);
        db.Entry(entity).State = EntityState.Modified;

        var entry = db.ChangeTracker.Entries<BaseEfEntity>().First();
        var (verb, severity) = FcmsAuditInterceptor.DeriveAction(entry);

        Assert.Equal("Deleted", verb);
        Assert.Equal(FcmsLogSeverity.Info, severity);
    }

    [Fact]
    public void DeriveAction_Deleted_state_returns_HardDeleted_Warning()
    {
        using var db = BuildDb();
        var entity = new FcmsPage { Id = Guid.NewGuid(), Title = "T", Slug = "s" };
        db.Set<FcmsPage>().Attach(entity);
        db.Entry(entity).State = EntityState.Deleted;

        var entry = db.ChangeTracker.Entries<BaseEfEntity>().First();
        var (verb, severity) = FcmsAuditInterceptor.DeriveAction(entry);

        Assert.Equal("HardDeleted", verb);
        Assert.Equal(FcmsLogSeverity.Warning, severity);
    }

    // ── Opt-out rules ─────────────────────────────────────────────────────────

    [Fact]
    public void IAppendOnlyEntity_stub_is_flagged_correctly()
    {
        Assert.True(typeof(IAppendOnlyEntity).IsAssignableFrom(typeof(AppendOnlyStub)));
        Assert.False(typeof(AppendOnlyStub)
            .GetCustomAttributes(typeof(FcmsAuditIgnoreEntityAttribute), true).Any());
    }

    [Fact]
    public void FcmsAuditIgnoreEntity_attribute_is_detectable_on_type()
    {
        var attrs = typeof(FcmsIgnoredEntity)
            .GetCustomAttributes(typeof(FcmsAuditIgnoreEntityAttribute), inherit: true);
        Assert.NotEmpty(attrs);
    }

    [Fact]
    public void FcmsAuditEntity_attribute_exposes_correct_ActionPrefix()
    {
        var attr = (FcmsAuditEntityAttribute)typeof(FcmsAnnotatedEntity)
            .GetCustomAttributes(typeof(FcmsAuditEntityAttribute), inherit: true)
            .Single();
        Assert.Equal("BlogPost", attr.ActionPrefix);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static FcmsDbContext BuildDb()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FcmsDbContext(opts);
    }
}
