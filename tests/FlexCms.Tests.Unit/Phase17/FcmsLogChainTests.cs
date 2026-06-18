using FlexCms.Framework.Cms;

namespace FlexCms.Tests.Unit.Phase17;

/// <summary>
/// Regression tests for the audit-log hash chain — security-audit-recheck-2
/// E2E flagged two interacting bugs that broke verification on any database
/// round-trip:
///
/// 1. <see cref="FcmsLogChain.Compute"/> used <c>fffffff</c> (7 fractional
///    digits = 100-ns ticks). Postgres + MySQL only preserve 6 (microseconds),
///    so a hash computed at write time never matched after a round-trip.
/// 2. <c>FcmsDbContext.SaveChangesAsync</c> unconditionally stamped
///    CreatedAt = now, clobbering the per-row tick offset that
///    FcmsAuditInterceptor used to keep batch order deterministic.
///
/// These tests pin down the contract so neither regression silently
/// reappears.
/// </summary>
public class FcmsLogChainTests
{
    [Fact]
    public void Compute_uses_microsecond_precision_so_db_roundtrip_survives()
    {
        // .NET DateTime has 100-ns ticks (7 digits). Postgres/MySQL only
        // keep 6 digits (microseconds). The hash must not depend on the
        // 7th digit — if it does, every persisted row's recompute will
        // diverge from its stored hash and the verifier reports the
        // chain broken at row 1.

        // Two timestamps that differ only in the 7th fractional digit.
        // After a DB round-trip both quantize to the same microsecond.
        var row1 = new FcmsLog
        {
            CreatedAt = new DateTime(2026, 6, 19, 0, 0, 0, DateTimeKind.Utc).AddTicks(503535_3),  // .5035353 s
            UserId = Guid.Empty,
            Action = "A",
            EntityType = "T",
            EntityId = "1",
            Value = "v",
            PrevHash = null,
        };
        var row2 = new FcmsLog
        {
            CreatedAt = new DateTime(2026, 6, 19, 0, 0, 0, DateTimeKind.Utc).AddTicks(503535_8),  // .5035358 s
            UserId = row1.UserId,
            Action = row1.Action,
            EntityType = row1.EntityType,
            EntityId = row1.EntityId,
            Value = row1.Value,
            PrevHash = row1.PrevHash,
        };

        // Same hash → safe across DB precision loss.
        Assert.Equal(FcmsLogChain.Compute(row1), FcmsLogChain.Compute(row2));
    }

    [Fact]
    public void Compute_changes_when_any_field_changes()
    {
        // Sanity: the chain only collapses precision on the seventh digit.
        // Any actual field change must still produce a different hash —
        // otherwise tamper detection is worthless.
        var baseRow = new FcmsLog
        {
            CreatedAt = new DateTime(2026, 6, 19, 0, 0, 0, DateTimeKind.Utc).AddTicks(1234560),
            UserId = Guid.Empty,
            Action = "A",
            EntityType = "T",
            EntityId = "1",
            Value = "v",
            PrevHash = null,
        };
        var baseHash = FcmsLogChain.Compute(baseRow);

        Assert.NotEqual(baseHash, FcmsLogChain.Compute(WithAction("B")));
        Assert.NotEqual(baseHash, FcmsLogChain.Compute(WithEntityId("2")));
        Assert.NotEqual(baseHash, FcmsLogChain.Compute(WithValue("v2")));
        Assert.NotEqual(baseHash, FcmsLogChain.Compute(WithPrev("deadbeef")));
        // microsecond bump = different hash
        Assert.NotEqual(baseHash, FcmsLogChain.Compute(WithCreatedAt(baseRow.CreatedAt.AddTicks(10))));

        FcmsLog WithAction(string a) => Copy(b => b.Action = a);
        FcmsLog WithEntityId(string id) => Copy(b => b.EntityId = id);
        FcmsLog WithValue(string v) => Copy(b => b.Value = v);
        FcmsLog WithPrev(string p) => Copy(b => b.PrevHash = p);
        FcmsLog WithCreatedAt(DateTime t) => Copy(b => b.CreatedAt = t);
        FcmsLog Copy(Action<FcmsLog> mutate)
        {
            var c = new FcmsLog
            {
                CreatedAt = baseRow.CreatedAt,
                UserId = baseRow.UserId,
                Action = baseRow.Action,
                EntityType = baseRow.EntityType,
                EntityId = baseRow.EntityId,
                Value = baseRow.Value,
                PrevHash = baseRow.PrevHash,
            };
            mutate(c);
            return c;
        }
    }
}
