namespace Hookline.Audio.Tests;

public sealed class OnnxStemSeparatorModelTests
{
    [Fact]
    [Trait("Category", "ModelInference")]
    public async Task InstalledFourStemModelMatchesPublishedContract()
    {
        var modelPath = Environment.GetEnvironmentVariable(
            "HOOKLINE_FOUR_STEM_MODEL_PATH"
        );
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            return;
        }

        var format = new PcmAudioFormat(44_100, 16, 2);
        var audio = new byte[
            format.GetAlignedByteCount(
                TimeSpan.FromMilliseconds(100)
            )
        ];
        var duration = format.GetDuration(audio.Length);
        var selection = new AudioBufferSnapshot
        {
            TrackInstanceId = 11,
            Format = format,
            Audio = audio,
            RequestedStart = TimeSpan.Zero,
            RequestedEnd = duration,
            AvailableStart = TimeSpan.Zero,
            AvailableEnd = duration,
            IncludedRanges =
            [
                new AudioTimeRange(TimeSpan.Zero, duration),
            ],
        };

        var result = await new OnnxStemSeparator().SeparateAsync(
            selection,
            modelPath,
            StemModelCatalog.FourStem
        );

        Assert.Equal(4, result.Stems.Count);
        Assert.All(
            result.Stems,
            stem =>
                Assert.Equal(
                    selection.Audio.Length,
                    stem.Snapshot.Audio.Length
                )
        );
    }
}
