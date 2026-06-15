using System.Text.RegularExpressions;

namespace FlexCms.Framework.Helpers;

/// <summary>
/// Country-aware mobile number validation and normalization.
///
/// <para>
/// Built-in rules: <c>BD</c> (default), <c>IN</c>, <c>US</c>. Register your own
/// via <see cref="Register(string,FcmsPhoneCountryRule)"/> when you need a new
/// market — the registry is process-wide and thread-safe.
/// </para>
///
/// <para>
/// Every rule defines a country dial code, the local-format regex
/// (the user-typed digits without the dial code), and an optional national-
/// trunk prefix that must be stripped on normalize (e.g. India's leading "0").
/// Normalization always returns the canonical E.164-style form
/// <c>{dialCode}{nationalDigits}</c> without the leading "+" — callers can
/// prepend "+" or format as they wish.
/// </para>
/// </summary>
public static class FcmsPhoneHelper
{
    /// <summary>Default country used when no <c>country</c> argument is supplied.</summary>
    public const string DefaultCountry = "BD";

    private static readonly Dictionary<string, FcmsPhoneCountryRule> _rules = new(StringComparer.OrdinalIgnoreCase)
    {
        // Bangladesh: 11 digits starting with 01, all GP/Robi/Banglalink/Teletalk/Airtel prefixes.
        ["BD"] = new(
            DialCode: "880",
            TrunkPrefix: "0",
            NationalRegex: new Regex(@"^1[3-9]\d{8}$", RegexOptions.Compiled)),

        // India: 10 digits starting with 6-9.
        ["IN"] = new(
            DialCode: "91",
            TrunkPrefix: "0",
            NationalRegex: new Regex(@"^[6-9]\d{9}$", RegexOptions.Compiled)),

        // United States: 10 digits — NANP. Area code first digit 2-9, exchange first digit 2-9.
        ["US"] = new(
            DialCode: "1",
            TrunkPrefix: null,
            NationalRegex: new Regex(@"^[2-9]\d{2}[2-9]\d{6}$", RegexOptions.Compiled)),
    };

    /// <summary>Register or replace a country rule at runtime.</summary>
    public static void Register(string countryCode, FcmsPhoneCountryRule rule)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
            throw new ArgumentException("Country code is required.", nameof(countryCode));
        _rules[countryCode.ToUpperInvariant()] = rule;
    }

    /// <summary>True when the country has a registered rule.</summary>
    public static bool IsSupported(string countryCode)
        => !string.IsNullOrWhiteSpace(countryCode) && _rules.ContainsKey(countryCode);

    /// <summary>
    /// Returns the country rule, or <c>null</c> when <paramref name="countryCode"/>
    /// has no registered rule.
    /// </summary>
    public static FcmsPhoneCountryRule? GetRule(string countryCode)
        => _rules.TryGetValue(countryCode, out var rule) ? rule : null;

    /// <summary>
    /// Validates <paramref name="number"/> against the rule for
    /// <paramref name="country"/>. Accepts: bare local digits, local digits
    /// with national-trunk prefix, dial-code prefixed (with or without "+"),
    /// and freely formatted variants with spaces / hyphens / parentheses.
    /// </summary>
    public static bool IsValid(string? number, string country = DefaultCountry)
        => TryNormalize(number, out _, country);

    /// <summary>
    /// Normalizes <paramref name="number"/> into the canonical
    /// <c>{dialCode}{nationalDigits}</c> form for the given country. Returns
    /// <c>true</c> on success and writes the normalized value to
    /// <paramref name="normalized"/>. Returns <c>false</c> on any validation
    /// failure (unknown country, wrong length, non-matching prefix, etc).
    /// </summary>
    public static bool TryNormalize(string? number, out string normalized, string country = DefaultCountry)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(number)) return false;

        var rule = GetRule(country);
        if (rule is null) return false;

        // Strip everything except digits and a leading "+".
        var digits = StripFormatting(number);
        if (digits.Length == 0) return false;

        // 1. If it starts with the dial code, peel it off and validate the rest.
        if (digits.StartsWith(rule.DialCode, StringComparison.Ordinal))
        {
            var rest = digits[rule.DialCode.Length..];
            if (rule.NationalRegex.IsMatch(rest))
            {
                normalized = rule.DialCode + rest;
                return true;
            }
            return false;
        }

        // 2. If a national trunk prefix is configured and present, peel it off.
        if (rule.TrunkPrefix is not null
            && digits.StartsWith(rule.TrunkPrefix, StringComparison.Ordinal)
            && digits.Length > rule.TrunkPrefix.Length)
        {
            var rest = digits[rule.TrunkPrefix.Length..];
            if (rule.NationalRegex.IsMatch(rest))
            {
                normalized = rule.DialCode + rest;
                return true;
            }
        }

        // 3. Try the bare-local form.
        if (rule.NationalRegex.IsMatch(digits))
        {
            normalized = rule.DialCode + digits;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Convenience: returns the normalized number, or <c>null</c> when invalid.
    /// </summary>
    public static string? Normalize(string? number, string country = DefaultCountry)
        => TryNormalize(number, out var result, country) ? result : null;

    private static string StripFormatting(string number)
    {
        var sb = new System.Text.StringBuilder(number.Length);
        foreach (var c in number)
            if (c is >= '0' and <= '9') sb.Append(c);
        return sb.ToString();
    }
}

/// <summary>
/// Rule describing how to validate and normalize a mobile number for one
/// country. Constructed once and registered with
/// <see cref="FcmsPhoneHelper.Register(string,FcmsPhoneCountryRule)"/>.
/// </summary>
/// <param name="DialCode">Country dial code without "+" (e.g. "880" for Bangladesh).</param>
/// <param name="TrunkPrefix">Optional national trunk prefix stripped on normalize ("0" for BD/IN; null for US).</param>
/// <param name="NationalRegex">Regex matching the digits-only local form (after dial-code/trunk peel).</param>
public sealed record FcmsPhoneCountryRule(string DialCode, string? TrunkPrefix, Regex NationalRegex);
