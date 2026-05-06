using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Widgets;

/// <summary>
/// Maps a registered widget into a named zone in display order. Multiple
/// placements per zone are allowed (the manager renders them by
/// <see cref="SortOrder"/>); the same widget can be placed in multiple zones.
/// </summary>
public class FcmsWidgetPlacement : BaseEfEntity
{
    /// <summary>Matches <see cref="IFcmsWidget.Id"/>.</summary>
    public string WidgetId { get; set; } = "";

    /// <summary>Zone name — convention is PascalCase identifier (<c>Sidebar</c>, <c>DashboardCards</c>).</summary>
    public string Zone { get; set; } = "";

    public int SortOrder { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Per-placement config payload. Schema is defined by the widget; empty/null means defaults.</summary>
    public string? ConfigJson { get; set; }
}
