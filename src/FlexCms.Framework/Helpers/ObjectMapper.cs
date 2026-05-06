using System.Reflection;

namespace FlexCms.Framework.Helpers;

/// <summary>
/// Lightweight reflection-based property copier — a zero-dependency alternative
/// to AutoMapper for simple ViewModel → Entity (or vice-versa) mapping scenarios.
/// Only copies properties where the name AND type match exactly.
/// </summary>
public static class ObjectMapper
{
    /// <summary>
    /// Copies matching public instance properties from <paramref name="source"/>
    /// to <paramref name="destination"/> and returns <paramref name="destination"/>.
    /// </summary>
    public static TDest Map<TSrc, TDest>(TSrc source, TDest destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        var destProps = typeof(TDest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .ToDictionary(p => p.Name);

        foreach (var src in typeof(TSrc).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (destProps.TryGetValue(src.Name, out var dest) &&
                dest.PropertyType == src.PropertyType)
            {
                dest.SetValue(destination, src.GetValue(source));
            }
        }

        return destination;
    }
}
