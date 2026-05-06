using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Exports;
using FlexCms.Framework.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlexCms.Tests.Integration.Phase12;

/// <summary>
/// ExportProcessorService against EF in-memory: queue → run → done flow,
/// missing-handler failure path, exception-during-render failure path,
/// and Done rows are ignored on the next pass.
///
/// <para>
/// Each test rebuilds the SP from scratch with the handler set it needs as
/// singletons — keeps the resolver wiring simple. Both the test and the
/// processor scope share the same in-memory DB store via the pre-computed
/// <c>dbName</c>.
/// </para>
/// </summary>
public sealed class ExportProcessorServiceTests
{
    private sealed class StubStorage : IFcmsFileStorage
    {
        public Dictionary<string, byte[]> Files { get; } = new();
        public Task<string> SaveAsync(string relativePath, Stream content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            content.CopyTo(ms);
            Files[relativePath] = ms.ToArray();
            return Task.FromResult("/" + relativePath);
        }
        public Task DeleteAsync(string relativePath, CancellationToken ct = default)
        { Files.Remove(relativePath); return Task.CompletedTask; }
        public Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default)
            => Task.FromResult(Files.ContainsKey(relativePath));
    }

    private sealed class StaticHandler : IFcmsExportHandler
    {
        public string HandlerId { get; }
        public string DisplayName => HandlerId;
        public IReadOnlyList<ExportFormat> SupportedFormats { get; } = [ExportFormat.Csv, ExportFormat.Excel, ExportFormat.Pdf];
        private readonly Func<byte[]> _render;
        public StaticHandler(string id, Func<byte[]> render) { HandlerId = id; _render = render; }
        public string SuggestedFileName(ExportFormat f, string? p) => HandlerId;
        public Task<byte[]> RenderAsync(ExportFormat f, string? p, CancellationToken ct = default)
            => Task.FromResult(_render());
    }

    private sealed class TestRig : IAsyncDisposable
    {
        public ServiceProvider Sp { get; }
        public StubStorage Storage { get; } = new();
        public string DbName { get; }

        public TestRig(IEnumerable<IFcmsExportHandler> handlers)
        {
            DbName = Guid.NewGuid().ToString();
            var sc = new ServiceCollection();
            sc.AddLogging();
            sc.AddDbContext<FcmsDbContext>(o => o.UseInMemoryDatabase(DbName));
            sc.AddScoped<DbContext>(sp => sp.GetRequiredService<FcmsDbContext>());
            sc.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
            sc.AddScoped<IFcmsUnitOfWork, EfUnitOfWork>();
            sc.AddSingleton<IFcmsFileStorage>(Storage);
            foreach (var h in handlers) sc.AddSingleton(h);
            Sp = sc.BuildServiceProvider();
        }

        public ExportProcessorService BuildProcessor()
            => new(Sp.GetRequiredService<IServiceScopeFactory>(),
                   new ExportProcessorOptions(),
                   NullLogger<ExportProcessorService>.Instance);

        public async ValueTask DisposeAsync() => await Sp.DisposeAsync();
    }

    [Fact]
    public async Task ProcessOnceAsync_marks_pending_done_and_persists_bytes()
    {
        await using var rig = new TestRig([new StaticHandler("test.csv", () => "a,b,c\n1,2,3"u8.ToArray())]);

        await using (var seedScope = rig.Sp.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<FcmsDbContext>();
            db.PendingExports.Add(new FcmsPendingExport
            {
                HandlerId = "test.csv",
                Format = ExportFormat.Csv,
                Title = "Test export"
            });
            await db.SaveChangesAsync();
        }

#pragma warning disable CA2000
        await rig.BuildProcessor().ProcessOnceAsync(CancellationToken.None);
#pragma warning restore CA2000

        await using var verifyScope = rig.Sp.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<FcmsDbContext>();
        var job = await verifyDb.PendingExports.AsNoTracking().FirstAsync();
        Assert.Equal(ExportStatus.Done, job.ExportStatus);
        Assert.NotNull(job.DownloadUrl);
        Assert.True(job.FileSizeBytes > 0);
        Assert.Single(rig.Storage.Files);
    }

    [Fact]
    public async Task ProcessOnceAsync_marks_failed_when_handler_missing()
    {
        await using var rig = new TestRig([]);   // no handler registered

        await using (var seedScope = rig.Sp.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<FcmsDbContext>();
            db.PendingExports.Add(new FcmsPendingExport { HandlerId = "missing.id", Format = ExportFormat.Csv, Title = "Missing" });
            await db.SaveChangesAsync();
        }

#pragma warning disable CA2000
        await rig.BuildProcessor().ProcessOnceAsync(CancellationToken.None);
#pragma warning restore CA2000

        await using var verifyScope = rig.Sp.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<FcmsDbContext>();
        var job = await verifyDb.PendingExports.AsNoTracking().FirstAsync();
        Assert.Equal(ExportStatus.Failed, job.ExportStatus);
        Assert.Contains("No handler registered", job.FailureReason!);
    }

    [Fact]
    public async Task ProcessOnceAsync_marks_failed_when_handler_throws()
    {
        await using var rig = new TestRig([new StaticHandler("throwing", () => throw new InvalidOperationException("boom"))]);

        await using (var seedScope = rig.Sp.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<FcmsDbContext>();
            db.PendingExports.Add(new FcmsPendingExport { HandlerId = "throwing", Format = ExportFormat.Csv, Title = "Bad" });
            await db.SaveChangesAsync();
        }

#pragma warning disable CA2000
        await rig.BuildProcessor().ProcessOnceAsync(CancellationToken.None);
#pragma warning restore CA2000

        await using var verifyScope = rig.Sp.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<FcmsDbContext>();
        var job = await verifyDb.PendingExports.AsNoTracking().FirstAsync();
        Assert.Equal(ExportStatus.Failed, job.ExportStatus);
        Assert.Equal("boom", job.FailureReason);
    }

    [Fact]
    public async Task ProcessOnceAsync_already_done_rows_are_ignored_on_next_pass()
    {
        var calls = 0;
        await using var rig = new TestRig([new StaticHandler("counter", () => { calls++; return [0x1]; })]);

        await using (var seedScope = rig.Sp.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<FcmsDbContext>();
            db.PendingExports.Add(new FcmsPendingExport { HandlerId = "counter", Format = ExportFormat.Csv, Title = "x" });
            await db.SaveChangesAsync();
        }

#pragma warning disable CA2000
        await rig.BuildProcessor().ProcessOnceAsync(CancellationToken.None);
        await rig.BuildProcessor().ProcessOnceAsync(CancellationToken.None);
#pragma warning restore CA2000

        Assert.Equal(1, calls);
    }
}
