using FlexCms.Framework.Security;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace FlexCms.Tests.Unit.Phase9;

public class HoneypotTests
{
    private readonly FcmsHoneypotService _svc = new();

    [Fact]
    public void FieldName_is_fcms_hp() => Assert.Equal("fcms_hp", _svc.FieldName);

    [Fact]
    public void IsLegit_returns_true_when_field_absent()
    {
        Assert.True(_svc.IsLegit(new Dictionary<string, StringValues>()));
    }

    [Fact]
    public void IsLegit_returns_true_when_field_empty()
    {
        var form = new Dictionary<string, StringValues> { ["fcms_hp"] = new StringValues("") };
        Assert.True(_svc.IsLegit(form));
    }

    [Fact]
    public void IsLegit_returns_false_when_field_filled()
    {
        var form = new Dictionary<string, StringValues> { ["fcms_hp"] = new StringValues("bot wuz here") };
        Assert.False(_svc.IsLegit(form));
    }

    [Fact]
    public void IsLegit_returns_true_when_form_is_null()
    {
        Assert.True(_svc.IsLegit(null!));
    }
}
