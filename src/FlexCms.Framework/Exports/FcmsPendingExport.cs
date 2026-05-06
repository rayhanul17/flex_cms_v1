using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Exports;

public enum ExportFormat
{
    Csv = 0,
    Excel = 1,
    Pdf = 2
}

public enum ExportStatus
{
    Pending = 0,
    Running = 1,
    Done = 2,
    Failed = 3
}

/// <summary>
/// Restart-safe heavy-export job. The admin UI inserts rows; the
/// <see cref="ExportProcessorService"/> picks them up on a 30s poll, hands
/// them to the matching <see cref="IFcmsExportHandler"/>, writes the
/// resulting bytes through <see cref="Storage.IFcmsFileStorage"/>, and then
/// fires an in-app notification with the download URL.
/// </summary>
public class FcmsPendingExport : BaseEfEntity
{
    /// <summary>Handler key — matches <see cref="IFcmsExportHandler.HandlerId"/>.</summary>
    public string HandlerId { get; set; } = "";

    public ExportFormat Format { get; set; } = ExportFormat.Csv;

    /// <summary>JSON parameters for the handler (filters, date ranges, etc.). Schema is the handler's choice.</summary>
    public string? ParametersJson { get; set; }

    public Guid? RequestedByUserId { get; set; }

    /// <summary>Display title — surfaces in the admin export list and the completion notification.</summary>
    public string Title { get; set; } = "";

    public ExportStatus ExportStatus { get; set; } = ExportStatus.Pending;

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? FailureReason { get; set; }

    /// <summary>Public download URL — populated when <see cref="ExportStatus"/> = <see cref="ExportStatus.Done"/>.</summary>
    public string? DownloadUrl { get; set; }
    public long? FileSizeBytes { get; set; }
}
