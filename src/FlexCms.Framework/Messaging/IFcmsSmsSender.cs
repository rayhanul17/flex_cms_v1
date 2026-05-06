namespace FlexCms.Framework.Messaging;

/// <summary>
/// Single-shot SMS send. Like <see cref="IFcmsEmailService"/> this never throws
/// for transport-level errors — it returns a populated <see cref="SmsSendResult"/>
/// so retry orchestration at the queue layer can decide what to do.
///
/// <para>
/// The default DI registration is the dispatcher <see cref="DispatchingSmsSender"/>,
/// which inspects <see cref="SmsSettings.Gateway"/> and forwards to one of the
/// concrete <see cref="ISmsGateway"/> implementations.
/// </para>
/// </summary>
public interface IFcmsSmsSender
{
    Task<SmsSendResult> SendAsync(SmsMessage message, CancellationToken ct = default);
}

/// <summary>Per-gateway transport. Implementations target a single SMS provider.</summary>
public interface ISmsGateway
{
    /// <summary>Identifier — must match a value from <see cref="SmsGateways"/>.</summary>
    string GatewayId { get; }

    Task<SmsSendResult> SendAsync(SmsMessage message, SmsSettings settings, string apiKey, CancellationToken ct = default);
}

public record SmsMessage(string To, string Text);

public record SmsSendResult(bool Success, string? Error = null, string? RawResponse = null)
{
    public static SmsSendResult Ok(string? raw = null) => new(true, null, raw);
    public static SmsSendResult Fail(string error, string? raw = null) => new(false, error, raw);
}
