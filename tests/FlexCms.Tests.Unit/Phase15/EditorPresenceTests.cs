using FlexCms.Framework.Cms.Editing;
using Xunit;

namespace FlexCms.Tests.Unit.Phase15;

public class EditorPresenceTests
{
    [Fact]
    public void Heartbeat_then_GetActive_returns_the_user()
    {
        var svc = new EditorPresenceService();
        var pageId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        svc.Heartbeat("FcmsPage", pageId, userA, "Alice");
        var active = svc.GetActive("FcmsPage", pageId);
        Assert.Single(active);
        Assert.Equal(userA, active[0].UserId);
        Assert.Equal("Alice", active[0].UserName);
    }

    [Fact]
    public void Multiple_users_on_same_entity_all_listed()
    {
        var svc = new EditorPresenceService();
        var pageId = Guid.NewGuid();
        svc.Heartbeat("FcmsPage", pageId, Guid.NewGuid(), "Alice");
        svc.Heartbeat("FcmsPage", pageId, Guid.NewGuid(), "Bob");
        svc.Heartbeat("FcmsPage", pageId, Guid.NewGuid(), "Carol");
        Assert.Equal(3, svc.GetActive("FcmsPage", pageId).Count);
    }

    [Fact]
    public void Different_entities_isolated()
    {
        var svc = new EditorPresenceService();
        svc.Heartbeat("FcmsPage", Guid.NewGuid(), Guid.NewGuid(), "Alice");
        // No heartbeat on this other page → empty.
        Assert.Empty(svc.GetActive("FcmsPage", Guid.NewGuid()));
    }

    [Fact]
    public void Release_removes_the_user_from_active_list()
    {
        var svc = new EditorPresenceService();
        var pageId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        svc.Heartbeat("FcmsPage", pageId, userA, "Alice");
        svc.Release("FcmsPage", pageId, userA);
        Assert.Empty(svc.GetActive("FcmsPage", pageId));
    }

    [Fact]
    public void Different_entityType_treated_as_different_entity()
    {
        var svc = new EditorPresenceService();
        var sharedId = Guid.NewGuid();
        svc.Heartbeat("FcmsPage", sharedId, Guid.NewGuid(), "Alice");
        // Same id but different entity type → no overlap.
        Assert.Empty(svc.GetActive("FcmsPost", sharedId));
        Assert.Single(svc.GetActive("FcmsPage", sharedId));
    }

    [Fact]
    public void Repeat_heartbeat_replaces_LastSeen_not_duplicates_user()
    {
        var svc = new EditorPresenceService();
        var pageId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        svc.Heartbeat("FcmsPage", pageId, userA, "Alice");
        svc.Heartbeat("FcmsPage", pageId, userA, "Alice (renamed)");
        var active = svc.GetActive("FcmsPage", pageId);
        Assert.Single(active);
        Assert.Equal("Alice (renamed)", active[0].UserName);
    }
}
