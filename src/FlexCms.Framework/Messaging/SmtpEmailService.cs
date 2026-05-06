using FlexCms.Framework.Messaging.Services;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace FlexCms.Framework.Messaging;

/// <summary>
/// MailKit-backed implementation that pulls live SMTP credentials per-call.
/// Returning <see cref="EmailSendResult.Fail"/> for transport errors instead of
/// throwing keeps retry handling at the queue layer simple.
/// </summary>
public sealed class SmtpEmailService : IFcmsEmailService
{
    private readonly ISmtpSettingsService _settings;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(ISmtpSettingsService settings, ILogger<SmtpEmailService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (message is null) return EmailSendResult.Fail("Message was null.");
        if (string.IsNullOrWhiteSpace(message.To)) return EmailSendResult.Fail("Recipient required.");

        var (cfg, password) = await _settings.GetWithPasswordAsync(ct);
        if (!cfg.Enabled) return EmailSendResult.Fail("SMTP not enabled.");
        if (string.IsNullOrWhiteSpace(cfg.Host)) return EmailSendResult.Fail("SMTP host not configured.");

        try
        {
            using var mime = BuildMime(message, cfg);
            using var client = new SmtpClient();
            client.Timeout = cfg.TimeoutSeconds * 1000;

            // STARTTLS for 587/25, implicit TLS (SSL) for 465; UseSsl=false → no TLS at all (lab only).
            var secure = cfg.UseSsl
                ? (cfg.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable)
                : SecureSocketOptions.None;

            await client.ConnectAsync(cfg.Host, cfg.Port, secure, ct);
            if (!string.IsNullOrEmpty(cfg.Username))
                await client.AuthenticateAsync(cfg.Username, password, ct);
            await client.SendAsync(mime, ct);
            await client.DisconnectAsync(quit: true, ct);

            return EmailSendResult.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SMTP send failed to {To}", message.To);
            return EmailSendResult.Fail(ex.Message);
        }
    }

    internal static MimeMessage BuildMime(EmailMessage m, SmtpSettings cfg)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(string.IsNullOrWhiteSpace(cfg.FromName) ? cfg.FromAddress : cfg.FromName, cfg.FromAddress));
        foreach (var addr in Split(m.To)) msg.To.Add(MailboxAddress.Parse(addr));
        if (!string.IsNullOrWhiteSpace(m.Cc))
            foreach (var addr in Split(m.Cc!)) msg.Cc.Add(MailboxAddress.Parse(addr));
        if (!string.IsNullOrWhiteSpace(m.Bcc))
            foreach (var addr in Split(m.Bcc!)) msg.Bcc.Add(MailboxAddress.Parse(addr));
        msg.Subject = m.Subject;

        var body = new BodyBuilder
        {
            HtmlBody = m.IsHtml ? m.Body : null,
            TextBody = m.IsHtml ? null : m.Body
        };
        msg.Body = body.ToMessageBody();
        return msg;
    }

    private static IEnumerable<string> Split(string commaSeparated)
        => commaSeparated.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
