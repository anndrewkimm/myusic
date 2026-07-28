using Hookline.Audio;

namespace Hookline.App.Catalog;

public static class ClipRetrimAvailability
{
    public static bool IsAvailable(
        ClipCatalogEntry entry,
        AudioBufferSnapshot snapshot
    )
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot.TrackInstanceId == entry.TrackInstanceId
            && !snapshot.Audio.IsEmpty
            && snapshot.AvailableStart is { } availableStart
            && snapshot.AvailableEnd is { } availableEnd
            && availableStart <= entry.TrimStart
            && availableEnd >= entry.TrimEnd;
    }
}
