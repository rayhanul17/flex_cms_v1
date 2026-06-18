namespace FlexCms.Framework.Modules;

/// <summary>
/// Pre-DI lookup of approved module DLL hashes. Used by
/// <see cref="ModuleManager.ScanAndLoad"/> to refuse loading a tampered
/// DLL before <c>Assembly.LoadFrom</c> runs — see
/// security-audit-recheck-report §8.1.
///
/// <para>
/// The store can't depend on EF/DI because module discovery happens
/// during service registration (before the DI container is built).
/// Implementations use raw ADO.NET against the framework's known
/// <c>fcms_module_records</c> table.
/// </para>
/// </summary>
public interface IModuleTrustStore
{
    /// <summary>
    /// Returns the stored SHA-256 hash (lowercase hex) of the module DLL
    /// that was approved at upload time, or <c>null</c> if no record
    /// exists yet (trust on first use). Implementations should treat
    /// any read failure as "no record" — the activator records the hash
    /// later, so a missing read is a self-healing condition.
    /// </summary>
    string? GetApprovedHash(string moduleId);

    /// <summary>True when the store can answer at all. Disabled stores
    /// (no DB configured, no connection string, schema not yet upgraded)
    /// degrade gracefully — module load proceeds without integrity check
    /// on first boot, then the activator records the hash for next time.</summary>
    bool IsAvailable { get; }
}

/// <summary>Null implementation — always returns "no record" and reports unavailable.</summary>
public sealed class NullModuleTrustStore : IModuleTrustStore
{
    public static readonly NullModuleTrustStore Instance = new();
    public string? GetApprovedHash(string moduleId) => null;
    public bool IsAvailable => false;
}
