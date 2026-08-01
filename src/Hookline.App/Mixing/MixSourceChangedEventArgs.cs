using Hookline.Audio;

namespace Hookline.App.Mixing;

public sealed class MixSourceChangedEventArgs : EventArgs
{
    public MixSourceChangedEventArgs(
        MixSourceSlot slot,
        ImportedAudioFile source
    )
    {
        Slot = slot;
        Source =
            source
            ?? throw new ArgumentNullException(nameof(source));
    }

    public MixSourceSlot Slot { get; }

    public ImportedAudioFile Source { get; }
}
