namespace Hookline.Audio;

public sealed record AudioCaptureOptions
{
    public TimeSpan BufferWindow { get; init; } =
        TimeSpan.FromMinutes(5);

    public TimeSpan StallTimeout { get; init; } =
        TimeSpan.FromSeconds(5);

    public TimeSpan StallCheckInterval { get; init; } =
        TimeSpan.FromSeconds(1);

    internal void Validate()
    {
        if (BufferWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(BufferWindow)
            );
        }

        if (StallTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(StallTimeout)
            );
        }

        if (StallCheckInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(StallCheckInterval)
            );
        }
    }
}
