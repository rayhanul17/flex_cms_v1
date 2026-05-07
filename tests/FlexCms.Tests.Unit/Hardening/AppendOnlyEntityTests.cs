using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using Xunit;

namespace FlexCms.Tests.Unit.Hardening;

/// <summary>
/// Marker semantics check — both repo backends inspect
/// <see cref="IAppendOnlyEntity"/> via reflection. If someone removes the
/// marker from FcmsLog/FcmsLogArchive, both EF and Mongo would silently
/// start hiding soft-deleted log rows. Lock the marker into the type
/// hierarchy with a quick assertion.
/// </summary>
public class AppendOnlyEntityTests
{
    [Fact]
    public void FcmsLog_implements_IAppendOnlyEntity()
    {
        Assert.True(typeof(IAppendOnlyEntity).IsAssignableFrom(typeof(FcmsLog)),
            "FcmsLog must implement IAppendOnlyEntity — losing the marker re-enables soft-delete filtering, hides deleted log rows on Mongo.");
    }

    [Fact]
    public void FcmsLogArchive_implements_IAppendOnlyEntity()
    {
        Assert.True(typeof(IAppendOnlyEntity).IsAssignableFrom(typeof(FcmsLogArchive)),
            "FcmsLogArchive must implement IAppendOnlyEntity for the same reason as FcmsLog.");
    }

    [Fact]
    public void Normal_entities_do_NOT_implement_IAppendOnlyEntity()
    {
        // Sanity check — if everyone got the marker, soft-delete would be
        // dead and the tests above would pass for the wrong reason.
        Assert.False(typeof(IAppendOnlyEntity).IsAssignableFrom(typeof(FcmsPage)));
        Assert.False(typeof(IAppendOnlyEntity).IsAssignableFrom(typeof(FcmsPost)));
        Assert.False(typeof(IAppendOnlyEntity).IsAssignableFrom(typeof(FcmsCategory)));
    }
}
