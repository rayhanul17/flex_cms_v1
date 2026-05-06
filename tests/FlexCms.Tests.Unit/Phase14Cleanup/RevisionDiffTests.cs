using FlexCms.Framework.Cms.Revisions;
using Xunit;

namespace FlexCms.Tests.Unit.Phase14Cleanup;

/// <summary>
/// Verifies the DiffPlex wrapper classifies inserted / deleted / unchanged
/// lines correctly for both inline and side-by-side diff modes.
/// </summary>
public class RevisionDiffTests
{
    private readonly DiffPlexRevisionDiffService _svc = new();

    [Fact]
    public void DiffInline_classifies_inserted_and_deleted_lines()
    {
        var oldText = "line 1\nline 2\nline 3";
        var newText = "line 1\nline 2 changed\nline 3\nline 4 brand new";

        var lines = _svc.DiffInline(oldText, newText);

        Assert.Contains(lines, l => l.Kind == DiffLineKind.Inserted && l.Text.Contains("brand new"));
        Assert.Contains(lines, l => l.Kind == DiffLineKind.Deleted && l.Text == "line 2");
        Assert.Contains(lines, l => l.Kind == DiffLineKind.Unchanged && l.Text == "line 1");
    }

    [Fact]
    public void DiffInline_identical_strings_have_no_inserted_or_deleted_lines()
    {
        var lines = _svc.DiffInline("a\nb\nc", "a\nb\nc");
        Assert.DoesNotContain(lines, l => l.Kind is DiffLineKind.Inserted or DiffLineKind.Deleted);
    }

    [Fact]
    public void DiffInline_handles_null_inputs_without_throwing()
    {
        var lines = _svc.DiffInline(null!, null!);
        Assert.NotNull(lines);
    }

    [Fact]
    public void DiffSideBySide_returns_two_lists()
    {
        var (oldLines, newLines) = _svc.DiffSideBySide("a\nb", "a\nc");
        Assert.NotEmpty(oldLines);
        Assert.NotEmpty(newLines);
    }
}
