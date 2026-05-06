using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace FlexCms.Framework.Cms.Revisions;

public enum DiffLineKind
{
    Unchanged = 0,
    Inserted = 1,
    Deleted = 2,
    Modified = 3
}

public sealed record DiffLine(int? OldLine, int? NewLine, string Text, DiffLineKind Kind);

public interface IRevisionDiffService
{
    /// <summary>Inline-diff between two revisions of the same entity. Lines are tagged Unchanged / Inserted / Deleted / Modified.</summary>
    IReadOnlyList<DiffLine> DiffInline(string oldText, string newText);

    /// <summary>Side-by-side diff: returns two parallel line lists with matching pairs.</summary>
    (IReadOnlyList<DiffLine> Old, IReadOnlyList<DiffLine> New) DiffSideBySide(string oldText, string newText);
}

/// <summary>
/// Wraps DiffPlex (MIT) into the framework's typed shape so views and admin
/// API endpoints don't have to know about the underlying lib.
/// </summary>
public sealed class DiffPlexRevisionDiffService : IRevisionDiffService
{
    private static readonly IDiffer Differ = new Differ();

    public IReadOnlyList<DiffLine> DiffInline(string oldText, string newText)
    {
        var builder = new InlineDiffBuilder(Differ);
        var model = builder.BuildDiffModel(oldText ?? "", newText ?? "");

        var lines = new List<DiffLine>(model.Lines.Count);
        foreach (var line in model.Lines)
            lines.Add(new DiffLine(
                OldLine: line.Position,
                NewLine: line.Position,
                Text: line.Text ?? "",
                Kind: ToKind(line.Type)));
        return lines;
    }

    public (IReadOnlyList<DiffLine> Old, IReadOnlyList<DiffLine> New) DiffSideBySide(string oldText, string newText)
    {
        var builder = new SideBySideDiffBuilder(Differ);
        var model = builder.BuildDiffModel(oldText ?? "", newText ?? "");

        var oldList = new List<DiffLine>(model.OldText.Lines.Count);
        var newList = new List<DiffLine>(model.NewText.Lines.Count);

        foreach (var l in model.OldText.Lines)
            oldList.Add(new DiffLine(l.Position, l.Position, l.Text ?? "", ToKind(l.Type)));
        foreach (var l in model.NewText.Lines)
            newList.Add(new DiffLine(l.Position, l.Position, l.Text ?? "", ToKind(l.Type)));

        return (oldList, newList);
    }

    private static DiffLineKind ToKind(ChangeType t) => t switch
    {
        ChangeType.Inserted => DiffLineKind.Inserted,
        ChangeType.Deleted => DiffLineKind.Deleted,
        ChangeType.Modified => DiffLineKind.Modified,
        _ => DiffLineKind.Unchanged
    };
}
