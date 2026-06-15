using FlexCms.Framework.Helpers;

namespace FlexCms.Tests.Unit.Helpers;

public class FcmsPhoneHelperTests
{
    // ── Bangladesh ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("01711123456")]
    [InlineData("8801711123456")]
    [InlineData("+8801711123456")]
    [InlineData("01711 123 456")]
    [InlineData("(017) 1112-3456")]
    public void Bd_valid_numbers_normalize_to_e164(string input)
    {
        Assert.True(FcmsPhoneHelper.TryNormalize(input, out var n));
        Assert.Equal("8801711123456", n);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("0171112345")]      // one digit short
    [InlineData("017111234567")]    // one digit too long
    [InlineData("02711123456")]     // 02XX not a mobile prefix
    [InlineData("01211123456")]     // 012 not in valid BD mobile prefix set
    public void Bd_invalid_numbers_rejected(string? input)
        => Assert.False(FcmsPhoneHelper.IsValid(input));

    // ── India ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("9876543210")]
    [InlineData("09876543210")]
    [InlineData("919876543210")]
    [InlineData("+919876543210")]
    public void In_valid_numbers_normalize(string input)
    {
        Assert.True(FcmsPhoneHelper.TryNormalize(input, out var n, "IN"));
        Assert.Equal("919876543210", n);
    }

    [Theory]
    [InlineData("5876543210")]      // first digit out of 6-9
    [InlineData("98765432")]        // too short
    public void In_invalid_numbers_rejected(string input)
        => Assert.False(FcmsPhoneHelper.IsValid(input, "IN"));

    // ── United States ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("4155552671")]
    [InlineData("14155552671")]
    [InlineData("+1 (415) 555-2671")]
    public void Us_valid_numbers_normalize(string input)
    {
        Assert.True(FcmsPhoneHelper.TryNormalize(input, out var n, "US"));
        Assert.Equal("14155552671", n);
    }

    [Theory]
    [InlineData("1155552671")]     // area code starts with 1 — invalid in NANP
    [InlineData("415555267")]      // 9 digits
    public void Us_invalid_numbers_rejected(string input)
        => Assert.False(FcmsPhoneHelper.IsValid(input, "US"));

    // ── Unknown country / registry ─────────────────────────────────────────

    [Fact]
    public void Unknown_country_returns_false()
        => Assert.False(FcmsPhoneHelper.IsValid("12345", "XX"));

    [Fact]
    public void IsSupported_returns_true_for_built_in_rules()
    {
        Assert.True(FcmsPhoneHelper.IsSupported("BD"));
        Assert.True(FcmsPhoneHelper.IsSupported("US"));
        Assert.False(FcmsPhoneHelper.IsSupported("XX"));
    }

    [Fact]
    public void Register_adds_new_country_rule()
    {
        FcmsPhoneHelper.Register("ZZ", new FcmsPhoneCountryRule(
            DialCode: "999",
            TrunkPrefix: null,
            NationalRegex: new System.Text.RegularExpressions.Regex(@"^\d{5}$")));

        Assert.True(FcmsPhoneHelper.TryNormalize("12345", out var n, "ZZ"));
        Assert.Equal("99912345", n);
    }

    [Fact]
    public void Normalize_returns_null_on_invalid()
        => Assert.Null(FcmsPhoneHelper.Normalize("garbage"));
}
