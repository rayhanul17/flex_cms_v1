using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;

namespace FlexCms.Framework.Helpers;

/// <summary>
/// Convention and utility helpers shared across the framework, core, and modules.
/// </summary>
public static class FcmsHelper
{
    // ── Entity naming ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the table / collection name for an entity following the project
    /// convention: <c>snake_case</c> + plural, prefixed by the owning module's
    /// <c>TablePrefix</c> (e.g. "fcms" for the framework, "blog" for a Blog
    /// module). The prefix is only prepended when it is not already part of
    /// the snake-cased name.
    /// </summary>
    public static string GetEntityName<T>(string modulePrefix = "")
        => GetEntityName(typeof(T), modulePrefix);

    /// <inheritdoc cref="GetEntityName{T}(string)" />
    public static string GetEntityName(Type type, string modulePrefix = "")
    {
        var attr = type.GetCustomAttribute<FcmsTableAttribute>();
        if (attr is not null) return attr.Name;

        var snake = ToSnakeCase(type.Name);

        if (!string.IsNullOrEmpty(modulePrefix))
        {
            var prefix = modulePrefix.ToLowerInvariant();
            if (!snake.StartsWith(prefix + "_", StringComparison.Ordinal) &&
                !snake.Equals(prefix, StringComparison.Ordinal))
            {
                snake = $"{prefix}_{snake}";
            }
        }

        return Pluralize(snake);
    }

    /// <summary>
    /// Convert PascalCase / camelCase to snake_case.
    /// "FcmsUser" → "fcms_user", "BlogPost" → "blog_post".
    /// </summary>
    public static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        var sb = new StringBuilder(name.Length + 8);
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                bool prevLower = i > 0 && char.IsLower(name[i - 1]);
                bool nextLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                if (i > 0 && (prevLower || nextLower))
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Naive English pluralizer for table names.
    /// "user" → "users", "category" → "categories", "address" → "addresses".
    /// </summary>
    public static string Pluralize(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;
        if (word.EndsWith("ss") || word.EndsWith("ch") || word.EndsWith("sh") ||
            word.EndsWith("x") || word.EndsWith("z"))
            return word + "es";
        if (word.EndsWith('s')) return word;
        if (word.EndsWith("y") && word.Length > 1 && !"aeiou".Contains(word[^2]))
            return word[..^1] + "ies";
        return word + "s";
    }

    // ── Enum helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Converts an enum to <c>Dictionary&lt;int, string&gt;</c> using
    /// <see cref="DescriptionAttribute"/> when present, falling back to the member name.
    /// Useful for building dropdown lists in Razor views.
    /// </summary>
    public static Dictionary<int, string> LoadEnumToDictionary<T>(
        List<int>? excludeList = null,
        bool includeAll = false)
        where T : struct, Enum
    {
        var result = new Dictionary<int, string>();
        if (includeAll) result[0] = "All";

        foreach (var value in Enum.GetValues<T>())
        {
            var key = (int)(object)value;
            if (excludeList is not null && excludeList.Contains(key)) continue;

            var desc = typeof(T).GetField(value.ToString())
                ?.GetCustomAttribute<DescriptionAttribute>()?.Description;
            result[key] = desc ?? value.ToString()!;
        }
        return result;
    }

    // ── Display string helpers ─────────────────────────────────────────────

    /// <summary>Returns <paramref name="emptyString"/> when the value is null or whitespace.</summary>
    public static string GetDisplayString(string? value, string emptyString = "-")
        => string.IsNullOrWhiteSpace(value) ? emptyString : value.Trim();

    /// <summary>Returns <paramref name="emptyString"/> when the value is null.</summary>
    public static string GetDisplayString(object? value, string emptyString = "-")
        => value is null ? emptyString : value.ToString()!;

    /// <summary>Returns "Yes" / "No" or <paramref name="emptyString"/> when null.</summary>
    public static string GetDisplayString(bool? value, string emptyString = "-")
        => value is null ? emptyString : (value.Value ? "Yes" : "No");

    /// <summary>Formats a nullable DateTime using <paramref name="format"/>.</summary>
    public static string GetDisplayString(DateTime? value, string format = "yyyy-MM-dd", string emptyString = "-")
        => value is null ? emptyString : value.Value.ToString(format);

    // ── String helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Inserts a space before each uppercase letter: "BlogPost" → "Blog Post".
    /// Useful for generating admin UI labels from class/property names.
    /// </summary>
    public static string FormatName(string name)
        => Regex.Replace(name, "([A-Z])", " $1").Trim();

    // ── Base64URL helpers ──────────────────────────────────────────────────

    /// <summary>URL-safe Base64 encode (no padding, URL-safe alphabet).</summary>
    public static string? Base64UrlEncode(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(value));
    }

    /// <summary>Decodes a URL-safe Base64 string produced by <see cref="Base64UrlEncode"/>.</summary>
    public static string? Base64UrlDecode(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(value)); }
        catch { return null; }
    }

    // ── DateTime helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Returns midnight (00:00:00) of the given date converted to UTC using
    /// the caller's UTC offset in minutes. Use for date-range queries against UTC-stored data.
    /// </summary>
    public static DateTime StartOfDayUtc(DateTime date, int utcOffsetMinutes = 0)
    {
        var local = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Unspecified);
        return local.AddMinutes(-utcOffsetMinutes);
    }

    /// <summary>
    /// Returns 23:59:59 of the given date converted to UTC using
    /// the caller's UTC offset in minutes. Use for date-range queries against UTC-stored data.
    /// </summary>
    public static DateTime EndOfDayUtc(DateTime date, int utcOffsetMinutes = 0)
    {
        var local = new DateTime(date.Year, date.Month, date.Day, 23, 59, 59, DateTimeKind.Unspecified);
        return local.AddMinutes(-utcOffsetMinutes);
    }
}

/// <summary>
/// Override the auto-generated entity name produced by <see cref="FcmsHelper.GetEntityName{T}(string)"/>.
/// Use sparingly — the default convention should cover almost everything.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class FcmsTableAttribute : Attribute
{
    public string Name { get; }
    public FcmsTableAttribute(string name) => Name = name;
}
