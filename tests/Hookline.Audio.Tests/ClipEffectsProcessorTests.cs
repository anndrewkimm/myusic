using System.Buffers.Binary;

namespace Hookline.Audio.Tests;

public sealed class ClipEffectsProcessorTests
{
    [Fact]
    public void NeutralSettingsReturnTheOriginalSnapshotUnchanged()
    {
        var source = CreateSnapshot(
            new PcmAudioFormat(1_000, 16, 1),
            [12, -34, 56, -78]
        );

        var processed = ClipEffectsProcessor.Process(
            source,
            new ClipEffectSettings()
        );

        Assert.Same(source, processed);
        Assert.True(source.Audio.Span.SequenceEqual(processed.Audio.Span));
    }

    [Theory]
    [InlineData(2, 50)]
    [InlineData(0.5, 200)]
    public void SpeedChangesDurationAndResamplesPcm(
        double speed,
        int expectedFrames
    )
    {
        var samples = Enumerable
            .Range(0, 100)
            .Select(index => (short)(index * 100))
            .ToArray();
        var source = CreateSnapshot(
            new PcmAudioFormat(1_000, 16, 1),
            samples
        );

        var processed = ClipEffectsProcessor.Process(
            source,
            new ClipEffectSettings
            {
                SpeedMultiplier = speed,
            }
        );

        Assert.Equal(expectedFrames, ReadSamples(processed).Length);
        Assert.Equal(
            TimeSpan.FromMilliseconds(expectedFrames),
            processed.Duration
        );
        if (speed == 2)
        {
            Assert.Equal(
                [0, 200, 400, 600],
                ReadSamples(processed)
                    .Take(4)
                    .Select(sample => (int)sample)
                    .ToArray()
            );
        }
    }

    [Fact]
    public void EqualizerBandBoostsItsCenterMoreThanDistantFrequencies()
    {
        const int sampleRate = 8_000;
        var format = new PcmAudioFormat(sampleRate, 16, 1);
        var low = CreateSine(sampleRate, 62, 1_000);
        var high = CreateSine(sampleRate, 2_000, 1_000);
        var curve = GraphicEqualizerCurve.Flat.WithGain(1, 12);

        var boostedLow = ClipEffectsProcessor.Process(
            CreateSnapshot(format, low),
            new ClipEffectSettings
            {
                EqualizerCurve = curve,
            }
        );
        var boostedHigh = ClipEffectsProcessor.Process(
            CreateSnapshot(format, high),
            new ClipEffectSettings
            {
                EqualizerCurve = curve,
            }
        );

        var lowRatio =
            Rms(ReadSamples(boostedLow).Skip(sampleRate / 4))
            / Rms(low.Skip(sampleRate / 4));
        var highRatio =
            Rms(ReadSamples(boostedHigh).Skip(sampleRate / 4))
            / Rms(high.Skip(sampleRate / 4));
        Assert.True(lowRatio > 2.5, $"Low-frequency ratio: {lowRatio}");
        Assert.True(highRatio < 1.25, $"High-frequency ratio: {highRatio}");
    }

    [Fact]
    public void MaximumEqualizerBoostClampsSamplesAndKeepsChannelsIndependent()
    {
        const int sampleRate = 8_000;
        var samples = new short[sampleRate * 2];
        for (var frame = 0; frame < sampleRate; frame++)
        {
            samples[frame * 2] = short.MaxValue;
            samples[(frame * 2) + 1] = 0;
        }

        var processed = ClipEffectsProcessor.Process(
            CreateSnapshot(
                new PcmAudioFormat(sampleRate, 16, 2),
                samples
            ),
            new ClipEffectSettings
            {
                EqualizerCurve = CreateMaximumCurve(),
            }
        );
        var output = ReadSamples(processed);

        Assert.All(
            output.Where((_, index) => index % 2 == 0),
            sample => Assert.InRange(sample, short.MinValue, short.MaxValue)
        );
        Assert.All(
            output.Where((_, index) => index % 2 == 1),
            sample => Assert.Equal(0, sample)
        );
    }

    [Fact]
    public void LoopingCrossfadesBoundariesWithoutGaps()
    {
        const int sampleRate = 1_000;
        var samples = Enumerable
            .Range(0, 100)
            .Select(
                index =>
                    (short)Math.Round(
                        -10_000 + (index * (20_000d / 99))
                    )
            )
            .ToArray();
        var source = CreateSnapshot(
            new PcmAudioFormat(sampleRate, 16, 1),
            samples
        );

        var processed = ClipEffectsProcessor.Process(
            source,
            new ClipEffectSettings { LoopCount = 2 }
        );
        var output = ReadSamples(processed);

        Assert.Equal(195, output.Length);
        Assert.Equal(TimeSpan.FromMilliseconds(195), processed.Duration);
        var largestJoinStep = output
            .Skip(93)
            .Take(11)
            .Zip(
                output.Skip(94).Take(11),
                (left, right) => Math.Abs(right - left)
            )
            .Max();
        var unprocessedJoinStep = Math.Abs(samples[^1] - samples[0]);
        Assert.True(largestJoinStep < unprocessedJoinStep / 2);
    }

    [Fact]
    public void ComposedEffectsUseSpeedThenEqualizerThenLoop()
    {
        var source = CreateSnapshot(
            new PcmAudioFormat(1_000, 16, 1),
            Enumerable
                .Range(0, 100)
                .Select(index => (short)(index * 10))
                .ToArray()
        );
        var settings = new ClipEffectSettings
        {
            SpeedMultiplier = 2,
            EqualizerCurve = EqualizerPresetCatalog.GetCurve(
                EqualizerPreset.BassBoost
            ),
            LoopCount = 2,
        };

        var processed = ClipEffectsProcessor.Process(source, settings);

        Assert.Equal(TimeSpan.FromMilliseconds(95), processed.Duration);
        Assert.Equal(
            processed.Duration,
            ClipEffectsProcessor.GetOutputDuration(source, settings)
        );
    }

    [Fact]
    public void ExpandedOutputIsBoundedByTheFiveMinuteCap()
    {
        var source = CreateSnapshot(
            new PcmAudioFormat(10, 16, 1),
            new short[100]
        );
        var settings = new ClipEffectSettings
        {
            SpeedMultiplier =
                ClipEffectSettings.MinimumSpeedMultiplier,
            EqualizerCurve = CreateMaximumCurve(),
            LoopCount = ClipEffectSettings.MaximumLoopCount,
        };

        var processed = ClipEffectsProcessor.Process(source, settings);

        Assert.Equal(
            ClipEffectSettings.MaximumExpandedDuration,
            processed.Duration
        );
    }

    [Fact]
    public async Task ExtremeCombinedSettingsStillExportSuccessfully()
    {
        const int sampleRate = 44_100;
        const int frameCount = sampleRate / 10;
        var stereo = new short[frameCount * 2];
        for (var frame = 0; frame < frameCount; frame++)
        {
            var sample = (short)Math.Round(
                24_000
                    * Math.Sin(
                        2 * Math.PI * 60 * frame / sampleRate
                    )
            );
            stereo[frame * 2] = sample;
            stereo[(frame * 2) + 1] = sample;
        }

        var processed = ClipEffectsProcessor.Process(
            CreateSnapshot(
                new PcmAudioFormat(sampleRate, 16, 2),
                stereo
            ),
            new ClipEffectSettings
            {
                SpeedMultiplier =
                    ClipEffectSettings.MinimumSpeedMultiplier,
                EqualizerCurve = CreateMaximumCurve(),
                LoopCount = ClipEffectSettings.MaximumLoopCount,
            }
        );
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"hookline-effects-{Guid.NewGuid():N}"
        );

        try
        {
            var result = await new Mp3ClipExporter().ExportAsync(
                processed,
                new ClipExportMetadata
                {
                    Title = "Effects",
                    Artist = "Hookline",
                    Album = string.Empty,
                },
                temporaryDirectory
            );

            Assert.True(File.Exists(result.OutputPath));
            Assert.Equal(processed.Duration, result.Duration);
            Assert.InRange(
                result.Duration,
                TimeSpan.Zero,
                ClipEffectSettings.MaximumExpandedDuration
            );
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(
                    temporaryDirectory,
                    recursive: true
                );
            }
        }
    }

    private static AudioBufferSnapshot CreateSnapshot(
        PcmAudioFormat format,
        IReadOnlyList<short> samples
    )
    {
        var audio = new byte[samples.Count * sizeof(short)];
        for (var index = 0; index < samples.Count; index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(
                audio.AsSpan(index * sizeof(short), sizeof(short)),
                samples[index]
            );
        }

        var duration = format.GetDuration(audio.Length);
        return new AudioBufferSnapshot
        {
            TrackInstanceId = 7,
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
    }

    private static GraphicEqualizerCurve CreateMaximumCurve() =>
        new(
            Enumerable.Repeat(
                GraphicEqualizerBands.MaximumGainDecibels,
                GraphicEqualizerBands.CenterFrequencies.Count
            )
        );

    private static short[] ReadSamples(AudioBufferSnapshot snapshot)
    {
        var audio = snapshot.Audio.Span;
        var samples = new short[audio.Length / sizeof(short)];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = BinaryPrimitives.ReadInt16LittleEndian(
                audio.Slice(index * sizeof(short), sizeof(short))
            );
        }

        return samples;
    }

    private static short[] CreateSine(
        int sampleRate,
        double frequency,
        double amplitude
    ) =>
        Enumerable
            .Range(0, sampleRate)
            .Select(
                index =>
                    (short)Math.Round(
                        amplitude
                            * Math.Sin(
                                2
                                * Math.PI
                                * frequency
                                * index
                                / sampleRate
                            )
                    )
            )
            .ToArray();

    private static double Rms(IEnumerable<short> samples)
    {
        var values = samples.Select(sample => (double)sample).ToArray();
        return Math.Sqrt(
            values.Sum(sample => sample * sample) / values.Length
        );
    }
}
