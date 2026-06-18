using System.Text.Json;
using FlexCms.Framework.Modules.Attributes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Cms;

/// <summary>
/// Append-only JSONL fallback sink for audit events that fail to write to
/// the DB (DB down, transient EF failure, etc.). Writes one row per line
/// to <c>App_Data/logs/audit-fallback-yyyyMMdd.log</c> next to the main
/// host logs so an operator can recover the trail by hand.
///
/// <para>
/// All file I/O is wrapped in best-effort try/catch — losing a fallback
/// write is worse than nothing but better than crashing the request. We
/// never throw upwards.
/// </para>
/// </summary>
[FcmsScoped(typeof(IFcmsAuditFallbackSink))]
public sealed class FcmsAuditFallbackSink : IFcmsAuditFallbackSink
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<FcmsAuditFallbackSink> _logger;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
    };

    public FcmsAuditFallbackSink(IWebHostEnvironment env, ILogger<FcmsAuditFallbackSink> logger)
    {
        _env = env;
        _logger = logger;
    }

    public async Task WriteAsync(FcmsAuditFallbackEntry entry, CancellationToken ct = default)
    {
        try
        {
            var logsDir = Path.Combine(_env.ContentRootPath, "App_Data", "logs");
            Directory.CreateDirectory(logsDir);
            var file = Path.Combine(logsDir, $"audit-fallback-{DateTime.UtcNow:yyyyMMdd}.log");
            var line = JsonSerializer.Serialize(entry, Json) + Environment.NewLine;
            await File.AppendAllTextAsync(file, line, ct);
        }
        catch (Exception ex)
        {
            // No more fallbacks below us — log to the application logger so
            // it at least lands in Serilog rolling files or stdout.
            _logger.LogError(ex, "Audit fallback sink: failed to write {Action} {EntityType} {EntityId}",
                entry.Action, entry.EntityType, entry.EntityId);
        }
    }
}

public interface IFcmsAuditFallbackSink
{
    Task WriteAsync(FcmsAuditFallbackEntry entry, CancellationToken ct = default);
}

public sealed record FcmsAuditFallbackEntry(
    DateTime CreatedAtUtc,
    string Action,
    string EntityType,
    string EntityId,
    string? UserId,
    string? UserName,
    string? UserIp,
    string Module,
    string Severity,
    string? Value,
    string Reason);
