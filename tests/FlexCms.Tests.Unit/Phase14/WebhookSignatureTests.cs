using FlexCms.Framework.Webhooks;
using Xunit;

namespace FlexCms.Tests.Unit.Phase14;

public class WebhookSignatureTests
{
    [Fact]
    public void Same_secret_and_body_produce_same_signature()
    {
        var s1 = WebhookDispatcher.ComputeSignature("topsecret", "{\"event\":\"post.published\"}");
        var s2 = WebhookDispatcher.ComputeSignature("topsecret", "{\"event\":\"post.published\"}");
        Assert.Equal(s1, s2);
    }

    [Fact]
    public void Signature_format_is_sha256_hex_lowercased()
    {
        var sig = WebhookDispatcher.ComputeSignature("k", "x");
        Assert.StartsWith("sha256=", sig, StringComparison.Ordinal);
        // 7-char prefix + 64-char SHA-256 hex
        Assert.Equal(7 + 64, sig.Length);
        Assert.Equal(sig, sig.ToLowerInvariant());
    }

    [Fact]
    public void Different_body_changes_signature()
    {
        var s1 = WebhookDispatcher.ComputeSignature("k", "a");
        var s2 = WebhookDispatcher.ComputeSignature("k", "b");
        Assert.NotEqual(s1, s2);
    }

    [Fact]
    public void Different_secret_changes_signature()
    {
        var s1 = WebhookDispatcher.ComputeSignature("k1", "x");
        var s2 = WebhookDispatcher.ComputeSignature("k2", "x");
        Assert.NotEqual(s1, s2);
    }

    [Fact]
    public void Empty_secret_or_body_does_not_throw()
    {
        Assert.NotEmpty(WebhookDispatcher.ComputeSignature("", ""));
    }
}
