using System.ComponentModel;

namespace FlexCms.Framework.Db;

/// <summary>
/// Lifecycle status for every <see cref="IBaseEntity"/>. Replaces the old
/// boolean <c>IsDeleted</c> flag with an explicit three-state model.
/// </summary>
/// <remarks>
/// <para>
/// Values are stable integers chosen to be visually self-documenting
/// (e.g. <c>404</c> for Deleted mirrors the HTTP "not found" intuition).
/// </para>
/// <para>
/// <see cref="DescriptionAttribute"/> annotations are picked up by
/// <c>FcmsHelper.GetEnumDescription</c> / <c>EnumToSelectList</c> so admin
/// dropdowns and badges render the human label without hardcoded switches.
/// </para>
/// </remarks>
public enum EntityStatus
{
    [Description("Inactive")]
    InActive = 0,
    [Description("Active")]
    Active = 1,
    [Description("Deleted")]
    Deleted = 404
}
