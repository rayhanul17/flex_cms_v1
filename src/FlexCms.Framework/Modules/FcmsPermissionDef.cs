namespace FlexCms.Framework.Modules;

/// <summary>
/// Permission declared by a module. Returned from <see cref="IFcmsModule.GetPermissions"/>
/// and upserted into the <c>fcms_permissions</c> table during module activation.
///
/// <para>
/// The framework prefixes <see cref="Key"/> with <c>{ModuleId}.</c> on seed so that two
/// modules can declare the same short key without colliding. A module that declares
/// <c>new FcmsPermissionDef("invest.create", "Create Investments", "Investments")</c>
/// from <c>FlexCms.Investment</c> ends up stored as <c>flexcms.investment.invest.create</c>.
/// </para>
/// </summary>
/// <param name="Key">Short permission key (no module prefix; framework prepends it).</param>
/// <param name="DisplayName">Human-readable label shown in the admin role editor.</param>
/// <param name="Group">Accordion group label (e.g. "Investments"). Empty groups fall back to the module name.</param>
public sealed record FcmsPermissionDef(string Key, string DisplayName, string Group = "");
