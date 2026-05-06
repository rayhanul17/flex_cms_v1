using FlexCms.Framework.Db;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Messaging;

public sealed class MessageProcessorOptions
{
    /// <summary>Wait between empty-result polls. Default 30 seconds.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Rows pulled per poll. Default 50.</summary>
    public int BatchSize { get; init; } = 50;

    /// <summary>Total send attempts before marking <see cref="MessageDeliveryStatus.Failed"/>. Default 3.</summary>
    public int MaxRetries { get; init; } = 3;
}

/// <summary>
/// Restart-safe message drainer. Loops every <c>PollInterval</c>, claims a
/// batch of <see cref="MessageDeliveryStatus.Pending"/> rows + still-retriable
/// <see cref="MessageDeliveryStatus.Failed"/> rows, and dispatches each via
/// <see cref="IFcmsEmailService"/> or <see cref="IFcmsSmsSender"/>. Increments
/// retry count + records last error on transport failure; marks Failed once
/// retries are exhausted.
/// </summary>
public sealed class MessageProcessorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly MessageProcessorOptions _options;
    private readonly ILogger<MessageProcessorService> _logger;

    public MessageProcessorService(
        IServiceScopeFactory scopes,
        MessageProcessorOptions options,
        ILogger<MessageProcessorService> logger)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run once immediately on startup — recovers anything that was queued
        // pre-restart without making the operator wait the first poll interval.
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "MessageProcessorService poll failed"); }

            try { await Task.Delay(_options.PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Visible for tests — single-pass drain with no delay loop.</summary>
    public async Task ProcessOnceAsync(CancellationToken ct)
    {
        // Async scope so async-only disposables (EfUnitOfWork, DbContext)
        // get DisposeAsync()'d cleanly when the scope unwinds.
        await using var scope = _scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var repo = sp.GetRequiredService<IRepository<FcmsPendingMessage>>();
        var uow = sp.GetRequiredService<IFcmsUnitOfWork>();
        var email = sp.GetService<IFcmsEmailService>();
        var sms = sp.GetService<IFcmsSmsSender>();

        var batch = await repo.FindAsync(
            m => m.DeliveryStatus == MessageDeliveryStatus.Pending
                 || (m.DeliveryStatus == MessageDeliveryStatus.Failed && m.RetryCount < _options.MaxRetries),
            ct);

        if (batch.Count == 0) return;

        // Order oldest-first so retries don't get starved by a flood of new pending items.
        foreach (var msg in batch.OrderBy(m => m.CreatedAt).Take(_options.BatchSize))
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                bool ok;
                string? error = null;

                if (msg.Channel == MessageChannel.Email && email is not null)
                {
                    var r = await email.SendAsync(new EmailMessage(msg.To, msg.Subject, msg.Body, msg.IsHtml), ct);
                    ok = r.Success;
                    error = r.Error;
                }
                else if (msg.Channel == MessageChannel.Sms && sms is not null)
                {
                    var r = await sms.SendAsync(new SmsMessage(msg.To, msg.Body), ct);
                    ok = r.Success;
                    error = r.Error;
                }
                else
                {
                    ok = false;
                    error = $"No transport registered for channel {msg.Channel}";
                }

                msg.LastAttemptAt = Clock.FcmsTime.Now;
                msg.RetryCount++;
                if (ok)
                {
                    msg.DeliveryStatus = MessageDeliveryStatus.Sent;
                    msg.LastError = null;
                }
                else
                {
                    msg.LastError = error;
                    msg.DeliveryStatus = msg.RetryCount >= _options.MaxRetries
                        ? MessageDeliveryStatus.Failed
                        : MessageDeliveryStatus.Pending;
                }

                await repo.UpdateAsync(msg, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pending message {Id} processing threw", msg.Id);
            }
        }

        await uow.SaveChangesAsync(ct);
    }
}
