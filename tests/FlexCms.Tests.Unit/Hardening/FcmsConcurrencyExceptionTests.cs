using FlexCms.Framework.Db;
using Xunit;

namespace FlexCms.Tests.Unit.Hardening;

/// <summary>
/// Pinning the exception contract — controllers catch
/// <see cref="FcmsConcurrencyException"/> and surface "another editor saved
/// first; refresh to merge". EF wraps DbUpdateConcurrencyException into it
/// for backend-neutral handling.
/// </summary>
public class FcmsConcurrencyExceptionTests
{
    [Fact]
    public void Carries_message()
    {
        var ex = new FcmsConcurrencyException("conflict on FcmsPage X");
        Assert.Equal("conflict on FcmsPage X", ex.Message);
    }

    [Fact]
    public void Carries_inner_exception_for_EF_wrapping()
    {
        var inner = new InvalidOperationException("EF DbUpdateConcurrencyException stand-in");
        var ex = new FcmsConcurrencyException("wrapped", inner);
        Assert.Same(inner, ex.InnerException);
    }
}
