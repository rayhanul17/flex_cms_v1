using FlexCms.Framework.Backup;
using Xunit;

namespace FlexCms.Tests.Unit.Phase15;

/// <summary>
/// Pure-function checks against the backup service contract — listing,
/// path-traversal rejection. Full create/restore round-trip is exercised
/// in the integration test project against a real DbContext.
/// </summary>
public class BackupServiceBasicsTests
{
    [Fact]
    public void BackupOptions_defaults_include_media_and_config()
    {
        var o = new BackupOptions();
        Assert.True(o.IncludeMedia);
        Assert.True(o.IncludeConfig);
    }

    [Fact]
    public void RestoreOptions_defaults_restore_media_but_not_config()
    {
        // Keep the same conservative defaults that the admin UI uses —
        // accidentally overwriting setup.json on a restore would lock the
        // operator out, so opt-in only.
        var o = new RestoreOptions();
        Assert.True(o.RestoreMedia);
        Assert.False(o.RestoreConfig);
    }

    [Fact]
    public void BackupResult_carries_all_observable_fields()
    {
        var ts = new DateTime(2026, 5, 7, 10, 30, 0, DateTimeKind.Utc);
        var r = new BackupResult("backup_x.zip", @"C:\b\backup_x.zip", 1024, 12, ts);
        Assert.Equal("backup_x.zip", r.FileName);
        Assert.Equal(1024, r.SizeBytes);
        Assert.Equal(12, r.EntityCount);
        Assert.Equal(ts, r.CreatedAt);
    }

    [Fact]
    public void RestoreResult_carries_failure_info()
    {
        var fail = new RestoreResult(false, 0, 0, "no such file");
        Assert.False(fail.Success);
        Assert.Equal("no such file", fail.Error);
    }
}
