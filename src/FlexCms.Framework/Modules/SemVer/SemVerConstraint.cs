namespace FlexCms.Framework.Modules.SemVer;

/// <summary>
/// Tiny SemVer parser + comparator just for module dependency constraints.
/// We don't need the full NuGet SemVer 2.0 (no pre-release labels, no
/// build metadata) — modules ship with simple <c>major.minor.patch</c>
/// versions and depend on each other with operators like <c>&gt;=1.2.0</c>.
///
/// <para>
/// Supported operator forms (in <see cref="ParseConstraint"/>):
/// </para>
/// <list type="bullet">
///   <item><c>1.2.3</c> — exact match</item>
///   <item><c>=1.2.3</c> — exact match (explicit)</item>
///   <item><c>&gt;=1.2.0</c></item>
///   <item><c>&lt;=2.0.0</c></item>
///   <item><c>&gt;1.2.0</c></item>
///   <item><c>&lt;2.0.0</c></item>
///   <item><c>^1.2.0</c> — same major (1.x)</item>
///   <item><c>~1.2.0</c> — same major+minor (1.2.x)</item>
/// </list>
///
/// <para>
/// Two forms for the dependency string itself:
/// <c>"BlogModule"</c> (any version) or <c>"BlogModule>=1.2.0"</c>.
/// </para>
/// </summary>
public sealed record SemVerConstraint(string Operator, SemVer Version)
{
    public bool IsSatisfiedBy(SemVer actual)
    {
        var cmp = actual.CompareTo(Version);
        return Operator switch
        {
            "" or "=" => cmp == 0,
            ">=" => cmp >= 0,
            "<=" => cmp <= 0,
            ">" => cmp > 0,
            "<" => cmp < 0,
            "^" => actual.Major == Version.Major && cmp >= 0,
            "~" => actual.Major == Version.Major && actual.Minor == Version.Minor && cmp >= 0,
            _ => false,
        };
    }

    /// <summary>Parse an operator + version string. Returns null on malformed input.</summary>
    public static SemVerConstraint? Parse(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();

        // Look for a 1- or 2-char operator prefix.
        var ops = new[] { ">=", "<=", ">", "<", "=", "^", "~" };
        foreach (var op in ops)
        {
            if (s.StartsWith(op, StringComparison.Ordinal))
            {
                var rest = s[op.Length..].Trim();
                var v = SemVer.Parse(rest);
                return v is null ? null : new SemVerConstraint(op, v.Value);
            }
        }

        // No operator → exact match.
        var ver = SemVer.Parse(s);
        return ver is null ? null : new SemVerConstraint("", ver.Value);
    }

    /// <summary>
    /// Parse a <c>DependsOn</c> entry. Returns the module id + (optional)
    /// version constraint. Format: <c>ModuleId</c> or <c>ModuleId{op}{ver}</c>
    /// where <c>{op}</c> is one of the supported operators.
    /// </summary>
    public static (string ModuleId, SemVerConstraint? Constraint) ParseDependency(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry)) return ("", null);
        entry = entry.Trim();

        // Find the first operator character that follows the module id.
        // Module ids are dotted/hyphenated identifiers; operators start at the
        // first occurrence of any of: > < = ^ ~.
        var idx = entry.IndexOfAny(['>', '<', '=', '^', '~']);
        if (idx < 0) return (entry, null);

        var moduleId = entry[..idx].Trim();
        var rest = entry[idx..];
        var constraint = Parse(rest);
        return (moduleId, constraint);
    }
}

public readonly record struct SemVer(int Major, int Minor, int Patch) : IComparable<SemVer>
{
    public static SemVer? Parse(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var parts = s.Trim().Split('.');
        if (parts.Length is < 1 or > 3) return null;
        if (!int.TryParse(parts[0], out var major) || major < 0) return null;
        var minor = 0; var patch = 0;
        if (parts.Length >= 2 && (!int.TryParse(parts[1], out minor) || minor < 0)) return null;
        if (parts.Length >= 3 && (!int.TryParse(parts[2], out patch) || patch < 0)) return null;
        return new SemVer(major, minor, patch);
    }

    public int CompareTo(SemVer other)
    {
        var c = Major.CompareTo(other.Major); if (c != 0) return c;
        c = Minor.CompareTo(other.Minor); if (c != 0) return c;
        return Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
