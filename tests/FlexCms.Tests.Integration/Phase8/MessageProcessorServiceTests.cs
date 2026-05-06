using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FlexCms.Tests.Integration.Phase8;

/// <summary>
/// Integration tests for MessageProcessorService against an EF in-memory
/// FcmsDbContext + FcmsPendingMessage rows. Email/SMS senders are stubbed
/// (NSubstitute) so we exercise the retry/state machine without a real network.
/// </summary>
public class MessageProcessorServiceTests : IDisposable
{
    private readonly FcmsDbContext _db;
    private readonly IServiceScopeFactory _scopes;
    private readonly IFcmsEmailService _email;
    private readonly IFcmsSmsSender _sms;

    public MessageProcessorServiceTests()
    {
        _email = Substitute.For<IFcmsEmailService>();
        _sms = Substitute.For<IFcmsSmsSender>();

        // Pre-compute the in-memory store name OUTSIDE the lambda — AddDbContext
        // re-evaluates the configuration delegate per scope, so calling
        // Guid.NewGuid() inside would give every scope its own isolated store.
        var dbName = Guid.NewGuid().ToString();
        var sc = new ServiceCollection();
        sc.AddDbContext<FcmsDbContext>(o => o.UseInMemoryDatabase(dbName));
        sc.AddScoped<DbContext>(sp => sp.GetRequiredService<FcmsDbContext>());
        sc.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        sc.AddScoped<IFcmsUnitOfWork, EfUnitOfWork>();
        // Register against the interface — AddSingleton<TImpl>(instance) keys
        // off the runtime proxy type, which means GetService<IFcmsEmailService>
        // would return null. Explicit interface registration fixes that.
        sc.AddSingleton<IFcmsEmailService>(_email);
        sc.AddSingleton<IFcmsSmsSender>(_sms);

        var sp = sc.BuildServiceProvider();
        _scopes = sp.GetRequiredService<IServiceScopeFactory>();

        // Pre-resolve a context for direct seeding/assertions
#pragma warning disable CA2000
        var rootScope = sp.CreateScope();
#pragma warning restore CA2000
        _db = rootScope.ServiceProvider.GetRequiredService<FcmsDbContext>();
    }

    public void Dispose() => _db.Dispose();

#pragma warning disable CA2000
    private MessageProcessorService Build(int maxRetries = 3)
        => new(_scopes, new MessageProcessorOptions { MaxRetries = maxRetries }, NullLogger<MessageProcessorService>.Instance);
#pragma warning restore CA2000

    [Fact]
    public async Task ProcessOnce_marks_pending_email_as_sent_on_success()
    {
        _email.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(EmailSendResult.Ok());

        _db.PendingMessages.Add(new FcmsPendingMessage
        {
            Channel = MessageChannel.Email,
            To = "x@y.z",
            Subject = "Hi",
            Body = "Body",
            IsHtml = true
        });
        await _db.SaveChangesAsync();

#pragma warning disable CA2000
        await Build().ProcessOnceAsync(CancellationToken.None);
#pragma warning restore CA2000

        var m = await _db.PendingMessages.AsNoTracking().FirstAsync();
        Assert.Equal(MessageDeliveryStatus.Sent, m.DeliveryStatus);
        Assert.Equal(1, m.RetryCount);
        Assert.NotNull(m.LastAttemptAt);
        Assert.Null(m.LastError);
    }

    [Fact]
    public async Task ProcessOnce_keeps_pending_with_incremented_retry_on_transient_failure()
    {
        _email.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(EmailSendResult.Fail("temp glitch"));

        _db.PendingMessages.Add(new FcmsPendingMessage { Channel = MessageChannel.Email, To = "x@y.z", Body = "b" });
        await _db.SaveChangesAsync();

#pragma warning disable CA2000
        await Build(maxRetries: 3).ProcessOnceAsync(CancellationToken.None);
#pragma warning restore CA2000

        var m = await _db.PendingMessages.AsNoTracking().FirstAsync();
        Assert.Equal(MessageDeliveryStatus.Pending, m.DeliveryStatus);   // still retriable
        Assert.Equal(1, m.RetryCount);
        Assert.Equal("temp glitch", m.LastError);
    }

    [Fact]
    public async Task ProcessOnce_marks_failed_after_max_retries_exhausted()
    {
        _email.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(EmailSendResult.Fail("nope"));

        _db.PendingMessages.Add(new FcmsPendingMessage
        {
            Channel = MessageChannel.Email,
            To = "x@y.z",
            Body = "b",
            RetryCount = 2,                               // one retry left
            DeliveryStatus = MessageDeliveryStatus.Failed // already-tried row that the processor still picks up
        });
        await _db.SaveChangesAsync();

#pragma warning disable CA2000
        await Build(maxRetries: 3).ProcessOnceAsync(CancellationToken.None);
#pragma warning restore CA2000

        var m = await _db.PendingMessages.AsNoTracking().FirstAsync();
        Assert.Equal(MessageDeliveryStatus.Failed, m.DeliveryStatus);
        Assert.Equal(3, m.RetryCount);   // exhausted
    }

    [Fact]
    public async Task ProcessOnce_dispatches_sms_through_sms_sender()
    {
        _sms.SendAsync(Arg.Any<SmsMessage>(), Arg.Any<CancellationToken>())
            .Returns(SmsSendResult.Ok());

        _db.PendingMessages.Add(new FcmsPendingMessage { Channel = MessageChannel.Sms, To = "01700000000", Body = "yo" });
        await _db.SaveChangesAsync();

#pragma warning disable CA2000
        await Build().ProcessOnceAsync(CancellationToken.None);
#pragma warning restore CA2000

        await _sms.Received(1).SendAsync(Arg.Is<SmsMessage>(m => m.To == "01700000000"), Arg.Any<CancellationToken>());
        var msg = await _db.PendingMessages.AsNoTracking().FirstAsync();
        Assert.Equal(MessageDeliveryStatus.Sent, msg.DeliveryStatus);
    }

    [Fact]
    public async Task ProcessOnce_no_pending_rows_is_a_noop()
    {
#pragma warning disable CA2000
        await Build().ProcessOnceAsync(CancellationToken.None);
#pragma warning restore CA2000   // must not throw
        Assert.Equal(0, await _db.PendingMessages.CountAsync());
    }

    [Fact]
    public async Task ProcessOnce_already_sent_rows_are_ignored()
    {
        _email.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(EmailSendResult.Ok());

        _db.PendingMessages.Add(new FcmsPendingMessage
        {
            Channel = MessageChannel.Email,
            To = "x@y.z",
            Body = "b",
            DeliveryStatus = MessageDeliveryStatus.Sent
        });
        await _db.SaveChangesAsync();

#pragma warning disable CA2000
        await Build().ProcessOnceAsync(CancellationToken.None);
#pragma warning restore CA2000

        await _email.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }
}
