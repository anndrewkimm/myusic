using Hookline.Audio;

namespace Hookline.App.Catalog;

public sealed class ClipTagEditor : IClipTagEditor
{
    private readonly Mp3TagEditor _editor = new();

    public void UpdateTitle(string filePath, string title) =>
        _editor.UpdateTitle(filePath, title);
}
