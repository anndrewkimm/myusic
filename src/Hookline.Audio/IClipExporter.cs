namespace Hookline.Audio;

public interface IClipExporter
{
    Task<ClipExportResult> ExportAsync(
        AudioBufferSnapshot selection,
        ClipExportMetadata metadata,
        string outputFolder,
        CancellationToken cancellationToken = default
    );
}
