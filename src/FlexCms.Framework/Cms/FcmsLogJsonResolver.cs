using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace FlexCms.Framework.Cms;

/// <summary>
/// JSON contract resolver used by <see cref="FcmsLogService"/> when serializing
/// the <c>value</c> snapshot for <c>FcmsLog.Value</c>. Filters out:
///
/// <list type="bullet">
///   <item><b>Navigation properties</b> — class-typed (other than <c>string</c>)
///         and generic collections of class types. Domain/EF entities can be
///         passed straight in without nulling FK refs manually.</item>
///   <item><b>Identity sensitive fields</b> — <c>PasswordHash</c>,
///         <c>SecurityStamp</c>, <c>ConcurrencyStamp</c>, <c>NormalizedUserName</c>,
///         <c>NormalizedEmail</c>, etc. Hardcoded so callers never accidentally
///         leak them.</item>
///   <item><b>Properties marked <see cref="FcmsLogIgnoreAttribute"/></b> —
///         module-defined explicit exclusions.</item>
/// </list>
///
/// Anonymous projections (<c>new { post.Title, post.Slug }</c>) pass through
/// unchanged since they only have scalar properties.
/// </summary>
public sealed class FcmsLogJsonResolver : DefaultJsonTypeInfoResolver
{
    private static readonly HashSet<string> AlwaysSkipNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // ASP.NET Core Identity sensitive fields
        "PasswordHash",
        "SecurityStamp",
        "ConcurrencyStamp",
        "NormalizedUserName",
        "NormalizedEmail",
        "TwoFactorEnabled",
        "AccessFailedCount",
        "LockoutEnabled",
        "LockoutEnd",
        "PhoneNumberConfirmed",
        "EmailConfirmed",
        // Cookie/auth tokens
        "AuthenticationToken",
        "Tokens",
        "Claims",
        "Logins",
    };

    public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        var info = base.GetTypeInfo(type, options);
        if (info.Kind != JsonTypeInfoKind.Object) return info;

        for (int i = info.Properties.Count - 1; i >= 0; i--)
        {
            var prop = info.Properties[i];

            if (ShouldSkip(prop))
                info.Properties.RemoveAt(i);
        }

        return info;
    }

    private static bool ShouldSkip(JsonPropertyInfo prop)
    {
        // 1. Always-skip Identity sensitive names
        if (AlwaysSkipNames.Contains(prop.Name)) return true;

        // 2. [FcmsLogIgnore] attribute
        var attrs = prop.AttributeProvider?.GetCustomAttributes(typeof(FcmsLogIgnoreAttribute), inherit: true);
        if (attrs is { Length: > 0 }) return true;

        var t = prop.PropertyType;

        // 3. Collection types — strip ONLY if element type is a nav (class).
        //    Returns early so List<string>/List<int> don't fall through to step 4.
        if (t != typeof(string) && typeof(IEnumerable).IsAssignableFrom(t))
        {
            var elementType = GetElementType(t);
            return elementType is not null && IsNavType(elementType);
        }

        // 4. Strip nav-property references (class types other than string)
        if (IsNavType(t)) return true;

        return false;
    }

    private static bool IsNavType(Type t)
    {
        if (t.IsValueType) return false;        // Guid, DateTime, enums, primitives
        if (t == typeof(string)) return false;  // strings stay
        if (Nullable.GetUnderlyingType(t) is not null) return false;
        // System types that look like leafs (Uri, etc.) — treat as scalar
        if (t.Namespace?.StartsWith("System") == true && !t.IsClass) return false;
        // Anonymous types (compiler-generated) — keep their properties
        if (t.IsClass && t.GetCustomAttributes(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false).Length > 0
            && t.Name.Contains("AnonymousType"))
            return false;

        return t.IsClass;
    }

    private static Type? GetElementType(Type collectionType)
    {
        if (collectionType.IsArray) return collectionType.GetElementType();

        var ienumerableT = collectionType.GetInterfaces()
            .Concat(new[] { collectionType })
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        return ienumerableT?.GetGenericArguments()[0];
    }
}
