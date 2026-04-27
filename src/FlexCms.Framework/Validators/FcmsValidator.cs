using System.Text.RegularExpressions;

namespace FlexCms.Framework.Validators;

public static class FcmsValidator
{
    private static readonly Regex EmailRegex = new(
        @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    // BD mobile: +8801[3-9]XXXXXXXX
    private static readonly Regex BdMobileRegex = new(
        @"^\+8801[3-9]\d{8}$",
        RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    public static bool IsValidEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && EmailRegex.IsMatch(email);

    public static bool IsValidBdMobile(string? mobile) =>
        !string.IsNullOrWhiteSpace(mobile) && BdMobileRegex.IsMatch(mobile);
}
