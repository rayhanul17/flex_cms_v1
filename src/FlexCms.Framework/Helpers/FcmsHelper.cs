using System.Reflection;
using System.Text;

namespace FlexCms.Framework.Helpers;

/// <summary>
/// Convention helpers shared across the framework, core, and modules.
/// </summary>
public static class FcmsHelper
{
    /// <summary>
    /// Returns the table / collection name for an entity following the project
    /// convention: <c>snake_case</c> + plural, prefixed by the owning module's
    /// <c>TablePrefix</c> (e.g. "fcms" for the framework, "blog" for a Blog
    /// module). The prefix is only prepended when it is not already part of
    /// the snake-cased name.
    /// </summary>
    /// <param name="modulePrefix">
    /// The owning module's table prefix (without trailing underscore).
    /// Pass an empty string to skip prefixing entirely.
    /// </param>
    /// <example>
    /// <code>
    /// FcmsHelper.GetEntityName&lt;FcmsUser&gt;("fcms")        // → "fcms_users"
    /// FcmsHelper.GetEntityName&lt;FcmsPermission&gt;("fcms")  // → "fcms_permissions" (already prefixed)
    /// FcmsHelper.GetEntityName&lt;BlogPost&gt;("blog")        // → "blog_posts" (already prefixed)
    /// FcmsHelper.GetEntityName&lt;Comment&gt;("blog")         // → "blog_comments" (prefix prepended)
    /// FcmsHelper.GetEntityName&lt;StudentRecord&gt;("school") // → "school_student_records"
    /// </code>
    /// </example>
    public static string GetEntityName<T>(string modulePrefix = "")
        => GetEntityName(typeof(T), modulePrefix);

    /// <inheritdoc cref="GetEntityName{T}(string)" />
    public static string GetEntityName(Type type, string modulePrefix = "")
    {
        // Allow an explicit override via [FcmsTable("custom_name")] later if needed.
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
    /// Convert PascalCase / camelCase to snake_case (lowercase, underscore-separated).
    /// "FcmsUser" → "fcms_user", "BlogPost" → "blog_post", "AbcDef" → "abc_def".
    /// Consecutive uppercase letters are treated as one segment to preserve
    /// readability of acronyms — "HTTPRequest" → "http_request" (not "h_t_t_p_request").
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
    /// Naive English pluralizer suitable for table names.
    /// "user" → "users", "category" → "categories", "address" → "addresses",
    /// "post" → "posts". Words already ending in "s" are left unchanged.
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
