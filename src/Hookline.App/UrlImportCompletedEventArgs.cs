using Hookline.Audio;

namespace Hookline.App;

internal sealed class UrlImportCompletedEventArgs(
    ImportedAudioFile importedAudio
) : EventArgs
{
    public ImportedAudioFile ImportedAudio { get; } =
        importedAudio
        ?? throw new ArgumentNullException(nameof(importedAudio));
}
