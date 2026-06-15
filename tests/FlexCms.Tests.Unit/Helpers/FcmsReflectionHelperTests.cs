using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Helpers;

namespace FlexCms.Tests.Unit.Helpers;

public class FcmsReflectionHelperTests
{
    private class Sample
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
        public int Count { get; set; }
        public List<string> Tags { get; set; } = [];
        public ChildEntity? Child { get; set; }
        public List<ChildEntity> Children { get; set; } = [];
    }

    private class ChildEntity : BaseEfEntity { public string Name { get; set; } = ""; }

    [Fact]
    public void GetIdValue_returns_typed_id()
    {
        var s = new Sample { Id = Guid.NewGuid() };
        Assert.Equal(s.Id, FcmsReflectionHelper.GetIdValue<Guid>(s));
    }

    [Fact]
    public void GetIdValue_returns_default_when_object_null()
        => Assert.Equal(Guid.Empty, FcmsReflectionHelper.GetIdValue<Guid>(null));

    [Fact]
    public void IsGenericList_true_for_list()
        => Assert.True(FcmsReflectionHelper.IsGenericList(typeof(List<int>)));

    [Fact]
    public void IsGenericList_false_for_array()
        => Assert.False(FcmsReflectionHelper.IsGenericList(typeof(int[])));

    [Fact]
    public void IsNonStringEnumerable_skips_string()
    {
        Assert.False(FcmsReflectionHelper.IsNonStringEnumerable(typeof(string)));
        Assert.True(FcmsReflectionHelper.IsNonStringEnumerable(typeof(int[])));
    }

    [Fact]
    public void IsBaseEntity_detects_BaseEfEntity_subclass()
        => Assert.True(FcmsReflectionHelper.IsBaseEntity(typeof(ChildEntity)));

    [Fact]
    public void CreateList_creates_typed_list()
    {
        var list = FcmsReflectionHelper.CreateList(typeof(int));
        Assert.IsType<List<int>>(list);
        Assert.Empty(list);
    }

    [Fact]
    public void GetNavProperties_finds_single_and_collection_navs()
    {
        var navs = FcmsReflectionHelper.GetNavProperties(typeof(Sample));
        Assert.Contains(navs, p => p.Name == "Child");
        Assert.Contains(navs, p => p.Name == "Children");
        Assert.DoesNotContain(navs, p => p.Name == "Tags");
    }

    [Fact]
    public void GetScalarProperties_finds_primitives_and_strings()
    {
        var scalars = FcmsReflectionHelper.GetScalarProperties(typeof(Sample));
        Assert.Contains(scalars, p => p.Name == "Title");
        Assert.Contains(scalars, p => p.Name == "Count");
        Assert.Contains(scalars, p => p.Name == "Id");
        Assert.DoesNotContain(scalars, p => p.Name == "Tags");
    }
}
