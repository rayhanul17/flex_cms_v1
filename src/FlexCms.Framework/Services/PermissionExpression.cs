namespace FlexCms.Framework.Services;

/// <summary>
/// Evaluates permission expressions against a set of keys the user holds.
/// Syntax: single key | "a&amp;b" (AND — must have all) | "a|b" (OR — must have one).
/// </summary>
public static class PermissionExpression
{
    public static bool Evaluate(string expr, IReadOnlySet<string> userKeys)
    {
        if (expr.Contains('&'))
            return expr.Split('&').All(k => userKeys.Contains(k.Trim()));

        if (expr.Contains('|'))
            return expr.Split('|').Any(k => userKeys.Contains(k.Trim()));

        return userKeys.Contains(expr.Trim());
    }
}
