namespace FlexCms.Framework.Messaging;

/// <summary>
/// Single-shot synchronous email send. Most callers should not use this
/// directly — instead enqueue via <see cref="IFcmsBackgroundQueue"/> or
/// persist a <see cref="FcmsPendingMessage"/>, both of which dispatch through
/// the registered email service when the worker drains.
/// </summary>
public interface IFcmsEmailService
{
    /// <summary>
    /// Send <paramref name="message"/> through the configured SMTP transport.
    /// Returns a populated <see cref="EmailSendResult"/> describing success or
    /// failure — the implementation never throws for transport-level errors so
    /// retry orchestration stays predictable.
    /// </summary>
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default);
}

public record EmailMessage(
    string To,
    string Subject,
    string Body,
    bool IsHtml = true,
    string? Cc = null,
    string? Bcc = null);

public record EmailSendResult(bool Success, string? Error = null)
{
    public static EmailSendResult Ok() => new(true);
    public static EmailSendResult Fail(string error) => new(false, error);
}
