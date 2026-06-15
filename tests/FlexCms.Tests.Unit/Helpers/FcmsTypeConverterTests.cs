using System.ComponentModel;
using FlexCms.Framework.Helpers;

namespace FlexCms.Tests.Unit.Helpers;

public class FcmsTypeConverterTests
{
    [Theory]
    [InlineData("42", 42)]
    [InlineData(" 42 ", 42)]
    [InlineData(null, 0)]
    [InlineData("not-a-number", 0)]
    public void ParseInt_handles_valid_and_invalid(string? input, int expected)
        => Assert.Equal(expected, FcmsTypeConverter.ParseInt(input));

    [Theory]
    [InlineData("true", true)]
    [InlineData("YES", true)]
    [InlineData("1", true)]
    [InlineData("on", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("no", false)]
    [InlineData("garbage", false)]
    [InlineData(null, false)]
    public void ParseBool_recognises_common_truthy_falsy_values(string? input, bool expected)
        => Assert.Equal(expected, FcmsTypeConverter.ParseBool(input));

    [Fact]
    public void ParseDecimal_uses_invariant_culture()
        => Assert.Equal(1.5m, FcmsTypeConverter.ParseDecimal("1.5"));

    [Fact]
    public void ParseGuid_returns_default_on_invalid()
        => Assert.Equal(Guid.Empty, FcmsTypeConverter.ParseGuid("garbage"));

    [Fact]
    public void ParseNullableInt_returns_null_on_invalid()
        => Assert.Null(FcmsTypeConverter.ParseNullableInt("garbage"));

    [Fact]
    public void ParseNullableInt_returns_value_on_valid()
        => Assert.Equal(42, FcmsTypeConverter.ParseNullableInt("42"));

    public enum Color { Red = 1, Green = 2, Blue = 3 }

    [Theory]
    [InlineData("Red", Color.Red)]
    [InlineData("red", Color.Red)]
    [InlineData("2", Color.Green)]
    [InlineData("invalid", Color.Red)] // falls back
    public void ParseEnum_accepts_name_or_id(string input, Color expected)
        => Assert.Equal(expected, FcmsTypeConverter.ParseEnum(input, Color.Red));

    [Fact]
    public void ParseDateTimeUtc_normalises_to_utc()
    {
        var dt = FcmsTypeConverter.ParseDateTimeUtc("2026-01-15T10:00:00Z");
        Assert.Equal(DateTimeKind.Utc, dt.Kind);
    }
}
