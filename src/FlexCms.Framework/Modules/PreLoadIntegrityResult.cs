namespace FlexCms.Framework.Modules;

/// <summary>
/// Outcome of <see cref="ModuleManager.PreLoadIntegrityCheck"/>. Tri-state
/// because the scanner needs three different reactions:
/// <list type="bullet">
/// <item><see cref="NotModule"/> — silently skip; never call
///   <c>Assembly.LoadFrom</c>. Common case: a transitive dep DLL sitting
///   next to a real module in <c>bin/Debug/net*/</c>.</item>
/// <item><see cref="ValidModule"/> — safe to load. Hash matches the trust
///   store, or trust-on-first-use is enabled and no record exists yet.</item>
/// <item><see cref="InvalidModule"/> — refused. Log + continue. Examples:
///   hash mismatch, missing ModuleId, unreadable metadata, or
///   trust-on-first-use disabled with no recorded hash.</item>
/// </list>
/// </summary>
public enum PreLoadIntegrityResult
{
    NotModule,
    ValidModule,
    InvalidModule,
}
