using Hookline.App.Catalog;
using Hookline.Audio;

namespace Hookline.App.Tests;

public sealed class ClipRetrimAvailabilityTests
{
    [Fact]
    public void OriginalSelectionIsAvailableOnlyWhileFullyBuffered()
    {
        var entry = CreateEntry();
        var available = CreateSnapshot(
            availableStart: TimeSpan.FromSeconds(8),
            availableEnd: TimeSpan.FromSeconds(20)
        );
        var startRolledOff = CreateSnapshot(
            availableStart: TimeSpan.FromSeconds(11),
            availableEnd: TimeSpan.FromSeconds(20)
        );

        Assert.True(
            ClipRetrimAvailability.IsAvailable(
                entry,
                available
            )
        );
        Assert.False(
            ClipRetrimAvailability.IsAvailable(
                entry,
                startRolledOff
            )
        );
        Assert.False(
            ClipRetrimAvailability.IsAvailable(
                entry,
                available with { TrackInstanceId = 99 }
            )
        );
        Assert.False(
            ClipRetrimAvailability.IsAvailable(
                entry,
                available with { Audio = ReadOnlyMemory<byte>.Empty }
            )
        );
    }

    [Fact]
    public void ImportedSyntheticIdNaturallyHasNoLiveBuffer()
    {
        var format = new PcmAudioFormat(100, 16, 1);
        var buffer = new RollingAudioBuffer(
            format,
            TimeSpan.FromSeconds(5)
        );
        var entry = CreateEntry() with
        {
            TrackInstanceId = -1,
        };

        var snapshot = buffer.Query(entry.TrackInstanceId);

        Assert.False(
            ClipRetrimAvailability.IsAvailable(entry, snapshot)
        );
    }

    private static ClipCatalogEntry CreateEntry() =>
        new()
        {
            Id = Guid.NewGuid(),
            DisplayTitle = "Clip",
            SourceTitle = "Track",
            SourceArtist = "Artist",
            SourceAlbum = "Album",
            ExportedAt = DateTimeOffset.UtcNow,
            FilePath = "clip.mp3",
            TrimStart = TimeSpan.FromSeconds(10),
            TrimEnd = TimeSpan.FromSeconds(14),
            Duration = TimeSpan.FromSeconds(4),
            TrackInstanceId = 72,
        };

    private static AudioBufferSnapshot CreateSnapshot(
        TimeSpan availableStart,
        TimeSpan availableEnd
    ) =>
        new()
        {
            TrackInstanceId = 72,
            Format = new PcmAudioFormat(100, 16, 1),
            Audio = new byte[2400],
            RequestedStart = availableStart,
            RequestedEnd = availableEnd,
            AvailableStart = availableStart,
            AvailableEnd = availableEnd,
        };
}
