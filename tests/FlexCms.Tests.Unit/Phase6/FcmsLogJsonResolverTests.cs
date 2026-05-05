using System.Text.Json;
using FlexCms.Framework.Cms;
using Xunit;

namespace FlexCms.Tests.Unit.Phase6;

public class FcmsLogJsonResolverTests
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        TypeInfoResolver = new FcmsLogJsonResolver(),
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
    };

    // ── Test fixtures ─────────────────────────────────────────────────────────

    private sealed class Child
    {
        public string Name { get; set; } = "";
    }

    private sealed class Sample
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
        public int Count { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool Active { get; set; }
        public DayOfWeek Day { get; set; }
        public Guid? ParentId { get; set; }

        // Should be stripped — nav property
        public Child? Parent { get; set; }

        // Should be stripped — collection of nav
        public List<Child> Children { get; set; } = new();

        // Kept — collection of strings
        public List<string> Tags { get; set; } = new();

        // Stripped — explicit attribute
        [FcmsLogIgnore] public string Secret { get; set; } = "hidden";
    }

    private sealed class WithIdentityFields
    {
        public string UserName { get; set; } = "";
        public string PasswordHash { get; set; } = "leaky-hash";
        public string SecurityStamp { get; set; } = "leaky-stamp";
        public string ConcurrencyStamp { get; set; } = "leaky-cs";
        public string NormalizedUserName { get; set; } = "leaky";
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Strips_nav_property_reference()
    {
        var s = new Sample { Title = "T", Parent = new Child { Name = "ParentName" } };
        var json = JsonSerializer.Serialize(s, Opts);
        // "Parent": ... should not appear (but "ParentId": is fine)
        Assert.DoesNotContain("\"Parent\":", json);
        Assert.DoesNotContain("ParentName", json);
    }

    [Fact]
    public void Strips_nav_property_collection()
    {
        var s = new Sample
        {
            Title = "T",
            Children = new() { new Child { Name = "ChildA" }, new Child { Name = "ChildB" } }
        };
        var json = JsonSerializer.Serialize(s, Opts);
        Assert.DoesNotContain("\"Children\":", json);
        Assert.DoesNotContain("ChildA", json);
    }

    [Fact]
    public void Keeps_scalar_collections_like_List_string()
    {
        var s = new Sample { Title = "T", Tags = new() { "csharp", "dotnet" } };
        var json = JsonSerializer.Serialize(s, Opts);
        Assert.Contains("\"Tags\":", json);
        Assert.Contains("csharp", json);
    }

    [Fact]
    public void Keeps_scalars_value_types_enum_and_nullable_guid()
    {
        var s = new Sample
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Title = "Hello",
            Count = 7,
            CreatedAt = new DateTime(2026, 5, 5),
            Active = true,
            Day = DayOfWeek.Tuesday,
            ParentId = Guid.Parse("22222222-2222-2222-2222-222222222222")
        };
        var json = JsonSerializer.Serialize(s, Opts);
        Assert.Contains("Hello", json);
        Assert.Contains("\"Count\":7", json);
        // Day enum may be serialized as int (2) by default — check key presence
        Assert.Contains("\"Day\":", json);
        Assert.Contains("22222222", json);
        Assert.Contains("11111111", json);
    }

    [Fact]
    public void Strips_FcmsLogIgnore_marked_property()
    {
        var s = new Sample { Title = "T", Secret = "DO_NOT_LOG_ME" };
        var json = JsonSerializer.Serialize(s, Opts);
        Assert.DoesNotContain("DO_NOT_LOG_ME", json);
        Assert.DoesNotContain("Secret", json);
    }

    [Fact]
    public void Strips_Identity_sensitive_fields()
    {
        var u = new WithIdentityFields { UserName = "alice" };
        var json = JsonSerializer.Serialize(u, Opts);
        Assert.Contains("alice", json);
        Assert.DoesNotContain("leaky-hash", json);
        Assert.DoesNotContain("leaky-stamp", json);
        Assert.DoesNotContain("leaky-cs", json);
        Assert.DoesNotContain("PasswordHash", json);
    }

    [Fact]
    public void Anonymous_projection_keeps_all_scalar_fields()
    {
        var anon = new { Title = "X", Slug = "x", Status = 1 };
        var json = JsonSerializer.Serialize(anon, Opts);
        Assert.Contains("Title", json);
        Assert.Contains("Slug", json);
        Assert.Contains("Status", json);
    }
}
