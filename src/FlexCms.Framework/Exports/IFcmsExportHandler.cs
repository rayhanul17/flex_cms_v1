namespace FlexCms.Framework.Exports;

/// <summary>
/// Modules ship one of these per exportable dataset. The handler reads the
/// parameters JSON, queries its data, and returns the rendered bytes (the
/// processor decides where to put them via <see cref="Storage.IFcmsFileStorage"/>).
///
/// <para>
/// Example: <c>StudentResultExportHandler</c> in a school module reads a
/// term-id from <paramref name="parametersJson"/>, fetches the matching
/// rows, hands them to <see cref="Documents.IFcmsExcelService"/>, and
/// returns the .xlsx bytes.
/// </para>
/// </summary>
public interface IFcmsExportHandler
{
    /// <summary>Stable id — matches <see cref="FcmsPendingExport.HandlerId"/>. Convention: <c>{module}.{dataset}</c>.</summary>
    string HandlerId { get; }

    /// <summary>Human-readable label shown in the admin export-picker UI.</summary>
    string DisplayName { get; }

    /// <summary>Formats this handler can produce. The admin UI offers only these to the requester.</summary>
    IReadOnlyList<ExportFormat> SupportedFormats { get; }

    /// <summary>Suggested filename (extension supplied by the processor based on Format).</summary>
    string SuggestedFileName(ExportFormat format, string? parametersJson);

    /// <summary>Render the export. Throw to mark the job as Failed.</summary>
    Task<byte[]> RenderAsync(ExportFormat format, string? parametersJson, CancellationToken ct = default);
}
