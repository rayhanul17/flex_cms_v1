namespace FlexCms.Framework.Widgets;

public interface IFcmsWidgetManager
{
    /// <summary>All registered widgets — what the admin can pick from when creating placements.</summary>
    IReadOnlyList<IFcmsWidget> RegisteredWidgets { get; }

    IFcmsWidget? Get(string widgetId);

    /// <summary>
    /// Render the rendered HTML of every enabled placement in <paramref name="zone"/>,
    /// concatenated in <see cref="FcmsWidgetPlacement.SortOrder"/> order. Missing
    /// widget registrations (placement points at a widget the host doesn't know
    /// about) are silently skipped — keeps stale rows from breaking the page.
    /// </summary>
    Task<string> RenderZoneAsync(string zone, CancellationToken ct = default);

    Task<List<FcmsWidgetPlacement>> GetPlacementsAsync(string? zone = null, CancellationToken ct = default);

    Task<FcmsWidgetPlacement> AddAsync(string widgetId, string zone, int sortOrder = 0, string? configJson = null, CancellationToken ct = default);

    Task UpdateAsync(FcmsWidgetPlacement placement, CancellationToken ct = default);

    Task DeleteAsync(Guid placementId, CancellationToken ct = default);

    /// <summary>Reorder a zone in one shot. <paramref name="orderedIds"/> = placement ids in target order.</summary>
    Task ReorderZoneAsync(string zone, IReadOnlyList<Guid> orderedIds, CancellationToken ct = default);
}
