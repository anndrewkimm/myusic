namespace Hookline.App.Catalog;

public interface IClipRetrimLauncher
{
    Task<ClipRetrimResult> OpenAsync(
        ClipCatalogEntry entry,
        CancellationToken cancellationToken = default
    );
}
