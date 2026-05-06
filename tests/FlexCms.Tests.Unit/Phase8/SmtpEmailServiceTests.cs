using FlexCms.Framework.Messaging;
using FlexCms.Framework.Messaging.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FlexCms.Tests.Unit.Phase8;

/// <summary>
/// Lightweight unit coverage for SmtpEmailService — verifies the configuration
/// guard rails (disabled, missing host, missing recipient) without spinning up
/// a real SMTP server. Actual SMTP transport behaviour belongs in an end-to-end
/// suite with a fake server (out of scope here).
/// </summary>
public class SmtpEmailServiceTests
{
    private static SmtpEmailService Build(SmtpSettings cfg, string password = "")
    {
        var settings = Substitute.For<ISmtpSettingsService>();
        settings.GetWithPasswordAsync(Arg.Any<CancellationToken>()).Returns((cfg, password));
        return new SmtpEmailService(settings, NullLogger<SmtpEmailService>.Instance);
    }

    [Fact]
    public async Task Returns_fail_for_null_message()
    {
        var svc = Build(new SmtpSettings { Enabled = true, Host = "h" });
        var r = await svc.SendAsync(null!);
        Assert.False(r.Success);
    }

    [Fact]
    public async Task Returns_fail_when_disabled()
    {
        var svc = Build(new SmtpSettings { Enabled = false });
        var r = await svc.SendAsync(new EmailMessage("a@b.c", "s", "b"));
        Assert.False(r.Success);
        Assert.Contains("not enabled", r.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Returns_fail_when_host_missing()
    {
        var svc = Build(new SmtpSettings { Enabled = true, Host = "" });
        var r = await svc.SendAsync(new EmailMessage("a@b.c", "s", "b"));
        Assert.False(r.Success);
        Assert.Contains("host", r.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Returns_fail_when_recipient_missing()
    {
        var svc = Build(new SmtpSettings { Enabled = true, Host = "h" });
        var r = await svc.SendAsync(new EmailMessage("", "s", "b"));
        Assert.False(r.Success);
    }

    [Fact]
    public void BuildMime_includes_html_body_when_IsHtml_true()
    {
        var cfg = new SmtpSettings { FromAddress = "from@example.com", FromName = "Sender" };
        var msg = SmtpEmailService.BuildMime(new EmailMessage("to@example.com", "Hi", "<b>x</b>", IsHtml: true), cfg);

        Assert.Equal("Hi", msg.Subject);
        Assert.Single(msg.To);
        Assert.Equal("to@example.com", ((MimeKit.MailboxAddress)msg.To[0]).Address);
        Assert.Contains("<b>x</b>", msg.HtmlBody, StringComparison.Ordinal);
        Assert.Null(msg.TextBody);
    }

    [Fact]
    public void BuildMime_uses_text_body_when_IsHtml_false()
    {
        var cfg = new SmtpSettings { FromAddress = "from@example.com" };
        var msg = SmtpEmailService.BuildMime(new EmailMessage("to@example.com", "Hi", "plain", IsHtml: false), cfg);
        Assert.Equal("plain", msg.TextBody);
        Assert.Null(msg.HtmlBody);
    }

    [Fact]
    public void BuildMime_splits_multiple_recipients_on_comma_and_semicolon()
    {
        var cfg = new SmtpSettings { FromAddress = "from@example.com" };
        var msg = SmtpEmailService.BuildMime(new EmailMessage("a@x.com, b@x.com; c@x.com", "Hi", "x"), cfg);
        Assert.Equal(3, msg.To.Count);
    }
}
