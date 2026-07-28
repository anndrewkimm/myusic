using System.Diagnostics;

namespace Hookline.App.Catalog;

public sealed class ExplorerClipRevealService : IClipRevealService
{
    public void Reveal(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var safePath = filePath.Replace("\"", string.Empty);
        Process.Start(
            new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{safePath}\"",
                UseShellExecute = true,
            }
        );
    }
}
