using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.FeatureFlags;

/// <summary>
/// Per-feature on/off + percentage-rollout + targeted-roles flag.
/// Stored as a single row per <see cref="Key"/> — small table, hot read,
/// cached in <see cref="IFcmsFeatureService"/>.
///
/// <para>
/// Evaluation precedence: <see cref="IsEnabled"/>=false → off; otherwise
/// <see cref="TargetRolesCsv"/> match → on; otherwise the user's stable hash
/// (mod 100) compared against <see cref="RolloutPercent"/>.
/// </para>
/// </summary>
public class FcmsFeatureFlag : BaseEfEntity
{
    /// <summary>Stable identifier — used in code: <c>features.IsEnabledAsync("ai-suggestions", userId)</c>.</summary>
    public string Key { get; set; } = "";

    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>Master switch. False → always off regardless of percent / target roles.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>0 = nobody; 100 = everyone (subject to IsEnabled).</summary>
    public int RolloutPercent { get; set; } = 100;

    /// <summary>Comma-separated role names that bypass the percent gate (always on for them).</summary>
    public string TargetRolesCsv { get; set; } = "";
}
