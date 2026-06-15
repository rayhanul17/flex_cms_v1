using System.Globalization;

namespace FlexCms.Framework.Helpers;

/// <summary>
/// Safe-by-default parsers — every method takes a possibly-null string from a
/// querystring / form / external API and returns the typed value or a
/// caller-supplied fallback. Never throws.
///
/// <para>
/// Compared to the BCL <c>TryParse</c> family these helpers:
/// </para>
/// <list type="bullet">
/// <item>Accept <c>null</c> / empty / whitespace and short-circuit to the fallback.</item>
/// <item>Use <see cref="CultureInfo.InvariantCulture"/> by default — numbers / dates from JSON / querystrings are culture-neutral.</item>
/// <item>Trim the input before parsing.</item>
/// <item>Treat common truthy/falsy strings the same way ("1" / "0" / "yes" / "no" / "true" / "false").</item>
/// </list>
/// </summary>
public static class FcmsTypeConverter
{
    public static int ParseInt(string? value, int fallback = 0)
        => int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public static long ParseLong(string? value, long fallback = 0)
        => long.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public static decimal ParseDecimal(string? value, decimal fallback = 0m)
        => decimal.TryParse(value?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public static double ParseDouble(string? value, double fallback = 0d)
        => double.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public static bool ParseBool(string? value, bool fallback = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var v = value.Trim().ToLowerInvariant();
        return v switch
        {
            "true" or "1" or "yes" or "y" or "on" => true,
            "false" or "0" or "no" or "n" or "off" => false,
            _ => fallback,
        };
    }

    public static Guid ParseGuid(string? value, Guid fallback = default)
        => Guid.TryParse(value?.Trim(), out var v) ? v : fallback;

    public static DateTime ParseDateTime(string? value, DateTime fallback = default)
        => DateTime.TryParse(value?.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var v) ? v : fallback;

    public static DateTime ParseDateTimeUtc(string? value, DateTime fallback = default)
        => DateTime.TryParse(value?.Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var v) ? v : fallback;

    /// <summary>
    /// Parses an enum value by name (case-insensitive) or by integer id. Returns
    /// <paramref name="fallback"/> on any failure.
    /// </summary>
    public static T ParseEnum<T>(string? value, T fallback = default) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var trimmed = value.Trim();
        if (Enum.TryParse<T>(trimmed, ignoreCase: true, out var named)) return named;
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            && Enum.IsDefined(typeof(T), id))
        {
            return (T)Enum.ToObject(typeof(T), id);
        }
        return fallback;
    }

    /// <summary>
    /// Returns the parsed value or <c>null</c> when <paramref name="value"/>
    /// is empty / unparsable — for nullable-bound view models.
    /// </summary>
    public static int? ParseNullableInt(string? value)
        => int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    public static decimal? ParseNullableDecimal(string? value)
        => decimal.TryParse(value?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;

    public static DateTime? ParseNullableDateTime(string? value)
        => DateTime.TryParse(value?.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var v) ? v : null;

    public static Guid? ParseNullableGuid(string? value)
        => Guid.TryParse(value?.Trim(), out var v) ? v : null;
}
