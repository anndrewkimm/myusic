namespace Hookline.App;

public sealed class AudioPreviewPositionChangedEventArgs(
    TimeSpan position
) : EventArgs
{
    public TimeSpan Position { get; } = position;
}
