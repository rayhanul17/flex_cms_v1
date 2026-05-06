using System.Text;
using FlexCms.Framework.Db;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Widgets;

public sealed class FcmsWidgetManager : IFcmsWidgetManager
{
    private readonly IRepository<FcmsWidgetPlacement> _repo;
    private readonly IFcmsUnitOfWork _uow;
    private readonly IServiceProvider _services;
    private readonly ILogger<FcmsWidgetManager> _logger;
    private readonly Dictionary<string, IFcmsWidget> _byId;

    public FcmsWidgetManager(
        IRepository<FcmsWidgetPlacement> repo,
        IFcmsUnitOfWork uow,
        IEnumerable<IFcmsWidget> widgets,
        IServiceProvider services,
        ILogger<FcmsWidgetManager> logger)
    {
        _repo = repo;
        _uow = uow;
        _services = services;
        _logger = logger;
        _byId = widgets.ToDictionary(w => w.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<IFcmsWidget> RegisteredWidgets => _byId.Values.ToArray();

    public IFcmsWidget? Get(string widgetId)
        => _byId.TryGetValue(widgetId ?? "", out var w) ? w : null;

    public async Task<string> RenderZoneAsync(string zone, CancellationToken ct = default)
    {
        var placements = (await _repo.FindAsync(p => p.Zone == zone && p.Enabled, ct))
            .OrderBy(p => p.SortOrder)
            .ToList();
        if (placements.Count == 0) return "";

        var sb = new StringBuilder();
        foreach (var p in placements)
        {
            if (!_byId.TryGetValue(p.WidgetId, out var widget))
            {
                _logger.LogWarning("FcmsWidgetManager: placement {PlacementId} points at unknown widget '{WidgetId}'", p.Id, p.WidgetId);
                continue;
            }

            try
            {
                sb.Append(await widget.RenderAsync(p.ConfigJson, _services, ct));
            }
            catch (Exception ex)
            {
                // One broken widget must not break the entire zone.
                _logger.LogError(ex, "Widget {WidgetId} threw while rendering", widget.Id);
            }
        }
        return sb.ToString();
    }

    public Task<List<FcmsWidgetPlacement>> GetPlacementsAsync(string? zone = null, CancellationToken ct = default)
        => string.IsNullOrEmpty(zone)
            ? _repo.FindAsync(p => true, ct)
            : _repo.FindAsync(p => p.Zone == zone, ct);

    public async Task<FcmsWidgetPlacement> AddAsync(string widgetId, string zone, int sortOrder = 0, string? configJson = null, CancellationToken ct = default)
    {
        var p = new FcmsWidgetPlacement { WidgetId = widgetId, Zone = zone, SortOrder = sortOrder, ConfigJson = configJson };
        await _repo.AddAsync(p, ct);
        await _uow.SaveChangesAsync(ct);
        return p;
    }

    public async Task UpdateAsync(FcmsWidgetPlacement placement, CancellationToken ct = default)
    {
        await _repo.UpdateAsync(placement, ct);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid placementId, CancellationToken ct = default)
    {
        var p = await _repo.GetByIdAsync(placementId, ct);
        if (p is null) return;
        await _repo.DeleteAsync(p, ct);   // hard delete — placements have no value as soft-deleted rows
        await _uow.SaveChangesAsync(ct);
    }

    public async Task ReorderZoneAsync(string zone, IReadOnlyList<Guid> orderedIds, CancellationToken ct = default)
    {
        if (orderedIds is null || orderedIds.Count == 0) return;
        var rows = await _repo.FindAsync(p => p.Zone == zone, ct);
        var byId = rows.ToDictionary(r => r.Id);

        for (int i = 0; i < orderedIds.Count; i++)
        {
            if (!byId.TryGetValue(orderedIds[i], out var p)) continue;
            p.SortOrder = i;
            await _repo.UpdateAsync(p, ct);
        }
        await _uow.SaveChangesAsync(ct);
    }
}
