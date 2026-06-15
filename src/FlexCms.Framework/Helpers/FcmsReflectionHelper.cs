using System.Collections;
using System.Reflection;
using FlexCms.Framework.Db;

namespace FlexCms.Framework.Helpers;

/// <summary>
/// Cross-cutting reflection helpers. Kept narrow on purpose — anything that
/// reaches into MVC metadata or EF model state should live next to the
/// consumer, not here.
/// </summary>
public static class FcmsReflectionHelper
{
    /// <summary>
    /// Reads the <c>Id</c> property from an object via reflection.
    /// Returns <c>default(TId)</c> when the property is missing or null.
    /// </summary>
    public static TId? GetIdValue<TId>(object? entity)
    {
        if (entity is null) return default;
        var prop = entity.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
        if (prop is null) return default;
        var raw = prop.GetValue(entity);
        if (raw is TId typed) return typed;
        if (raw is null) return default;
        try { return (TId)Convert.ChangeType(raw, typeof(TId)); }
        catch { return default; }
    }

    /// <summary>True when <paramref name="type"/> is a closed <c>List&lt;T&gt;</c>.</summary>
    public static bool IsGenericList(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);

    /// <summary>
    /// True when <paramref name="type"/> implements <see cref="IEnumerable"/> but
    /// is not <see cref="string"/>. Useful for "is this a collection property?"
    /// checks in generic UI/DTO code.
    /// </summary>
    public static bool IsNonStringEnumerable(Type type)
        => type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);

    /// <summary>
    /// True when <paramref name="type"/> is one of the framework's entity
    /// markers (<see cref="IBaseEntity"/> or any class deriving from it).
    /// </summary>
    public static bool IsBaseEntity(Type type)
        => typeof(IBaseEntity).IsAssignableFrom(type);

    /// <summary>
    /// Creates an empty <c>List&lt;<paramref name="elementType"/>&gt;</c> via
    /// reflection. Useful when you only know the element type at runtime
    /// (generic DataTables / CSV importers).
    /// </summary>
    public static IList CreateList(Type elementType)
    {
        var listType = typeof(List<>).MakeGenericType(elementType);
        return (IList)Activator.CreateInstance(listType)!;
    }

    /// <summary>
    /// Returns the public, instance properties on <paramref name="type"/> whose
    /// CLR type is a <see cref="IBaseEntity"/> or a collection of one. These are
    /// the "navigation" properties EF cares about when scaffolding includes /
    /// detail views.
    /// </summary>
    public static IReadOnlyList<PropertyInfo> GetNavProperties(Type type)
    {
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var result = new List<PropertyInfo>(props.Length);
        foreach (var p in props)
        {
            if (IsBaseEntity(p.PropertyType))
            {
                result.Add(p);
                continue;
            }
            if (IsNonStringEnumerable(p.PropertyType))
            {
                var elem = p.PropertyType.IsGenericType
                    ? p.PropertyType.GetGenericArguments().FirstOrDefault()
                    : p.PropertyType.GetElementType();
                if (elem is not null && IsBaseEntity(elem)) result.Add(p);
            }
        }
        return result;
    }

    /// <summary>
    /// Returns the public, instance properties on <paramref name="type"/> whose
    /// CLR type is a primitive, <see cref="string"/>, <see cref="DateTime"/>,
    /// <see cref="Guid"/>, <see cref="decimal"/>, or an enum — i.e. anything a
    /// generic edit form can render as a single input.
    /// </summary>
    public static IReadOnlyList<PropertyInfo> GetScalarProperties(Type type)
    {
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var result = new List<PropertyInfo>(props.Length);
        foreach (var p in props)
        {
            var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            if (t.IsPrimitive
                || t == typeof(string)
                || t == typeof(DateTime)
                || t == typeof(DateTimeOffset)
                || t == typeof(Guid)
                || t == typeof(decimal)
                || t.IsEnum)
            {
                result.Add(p);
            }
        }
        return result;
    }
}
