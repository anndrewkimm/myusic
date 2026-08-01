namespace Hookline.App.Mixing;

public sealed class MixSourceEditRequestedEventArgs(
    MixSourceSlot slot
) : EventArgs
{
    public MixSourceSlot Slot { get; } = slot;
}
