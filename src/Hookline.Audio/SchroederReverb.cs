using System.Runtime.InteropServices;

namespace Hookline.Audio;

internal static class SchroederReverb
{
    private const double CombFeedback = 0.82;
    private const double CombDamping = 0.25;
    private const double AllpassFeedback = 0.5;
    private const double StereoSpreadSeconds = 0.0005;
    private const int TargetProcessingSampleRate = 11_025;
    private static readonly double[] CombDelaySeconds =
        [0.0297, 0.0371, 0.0411, 0.0437];
    private static readonly double[] AllpassDelaySeconds =
        [0.005, 0.0017];

    public static readonly TimeSpan TailDuration =
        TimeSpan.FromSeconds(2);

    public static byte[] Apply(
        ReadOnlySpan<byte> source,
        PcmAudioFormat format,
        double wetMix,
        int outputFrameCount,
        CancellationToken cancellationToken
    )
    {
        var sourceFrameCount = source.Length / format.BlockAlign;
        var output = new byte[
            checked(outputFrameCount * format.BlockAlign)
        ];
        var sourceSamples = MemoryMarshal.Cast<byte, short>(
            source[
                ..(sourceFrameCount * format.BlockAlign)
            ]
        );
        var outputSamples =
            MemoryMarshal.Cast<byte, short>(output);
        var processingStride = Math.Max(
            1,
            format.SampleRate / TargetProcessingSampleRate
        );
        var processingSampleRate =
            format.SampleRate / processingStride;
        var reverbFrameCount =
            ((outputFrameCount + processingStride - 1)
                / processingStride)
            + 1;
        var dryMix = 1 - wetMix;

        for (
            var channel = 0;
            channel < format.Channels;
            channel++
        )
        {
            var reverb = new ReverbChannel(
                processingSampleRate,
                channel * StereoSpreadSeconds
            );
            var wetSamples = new float[reverbFrameCount];
            for (
                var reverbFrame = 0;
                reverbFrame < reverbFrameCount;
                reverbFrame++
            )
            {
                if ((reverbFrame & 0x3FFF) == 0)
                {
                    cancellationToken
                        .ThrowIfCancellationRequested();
                }

                var firstSourceFrame =
                    reverbFrame * processingStride;
                var sourceFrames = Math.Min(
                    processingStride,
                    sourceFrameCount - firstSourceFrame
                );
                double input = 0;
                if (sourceFrames > 0)
                {
                    for (
                        var offset = 0;
                        offset < sourceFrames;
                        offset++
                    )
                    {
                        input += sourceSamples[
                            (
                                (firstSourceFrame + offset)
                                * format.Channels
                            )
                            + channel
                        ];
                    }

                    input /= sourceFrames * 32768d;
                }

                wetSamples[reverbFrame] =
                    (float)reverb.Process(input);
            }

            for (var frame = 0; frame < outputFrameCount; frame++)
            {
                if ((frame & 0x3FFF) == 0)
                {
                    cancellationToken
                        .ThrowIfCancellationRequested();
                }

                var input =
                    frame < sourceFrameCount
                        ? sourceSamples[
                                (frame * format.Channels)
                                + channel
                            ]
                            / 32768d
                        : 0;
                var firstReverbFrame =
                    frame / processingStride;
                var fraction =
                    (frame % processingStride)
                    / (double)processingStride;
                var reverberated =
                    wetSamples[firstReverbFrame]
                    + (
                        (
                            wetSamples[firstReverbFrame + 1]
                            - wetSamples[firstReverbFrame]
                        )
                        * fraction
                    );
                outputSamples[
                    (frame * format.Channels) + channel
                ] =
                    (short)Math.Clamp(
                        Math.Round(
                            (
                                (input * dryMix)
                                + (reverberated * wetMix)
                            ) * 32768d
                        ),
                        short.MinValue,
                        short.MaxValue
                    );
            }
        }

        return output;
    }

    private sealed class ReverbChannel
    {
        private readonly CombFilter[] _combFilters;
        private readonly AllpassFilter[] _allpassFilters;

        public ReverbChannel(
            int sampleRate,
            double additionalDelaySeconds
        )
        {
            _combFilters = CombDelaySeconds
                .Select(
                    delay =>
                        new CombFilter(
                            GetDelayLength(
                                sampleRate,
                                delay + additionalDelaySeconds
                            )
                        )
                )
                .ToArray();
            _allpassFilters = AllpassDelaySeconds
                .Select(
                    delay =>
                        new AllpassFilter(
                            GetDelayLength(
                                sampleRate,
                                delay + additionalDelaySeconds
                            )
                        )
                )
                .ToArray();
        }

        public double Process(double input)
        {
            double combined = 0;
            foreach (var filter in _combFilters)
            {
                combined += filter.Process(input);
            }

            var output = combined / _combFilters.Length;
            foreach (var filter in _allpassFilters)
            {
                output = filter.Process(output);
            }

            return output;
        }

        private static int GetDelayLength(
            int sampleRate,
            double delaySeconds
        ) =>
            Math.Max(
                1,
                (int)Math.Round(sampleRate * delaySeconds)
            );
    }

    private sealed class CombFilter(int delayLength)
    {
        private readonly double[] _buffer = new double[delayLength];
        private int _index;
        private double _filterStore;

        public double Process(double input)
        {
            var output = _buffer[_index];
            _filterStore =
                (output * (1 - CombDamping))
                + (_filterStore * CombDamping);
            _buffer[_index] =
                input + (_filterStore * CombFeedback);
            _index++;
            if (_index == _buffer.Length)
            {
                _index = 0;
            }

            return output;
        }
    }

    private sealed class AllpassFilter(int delayLength)
    {
        private readonly double[] _buffer = new double[delayLength];
        private int _index;

        public double Process(double input)
        {
            var delayed = _buffer[_index];
            var output = delayed - input;
            _buffer[_index] =
                input + (delayed * AllpassFeedback);
            _index++;
            if (_index == _buffer.Length)
            {
                _index = 0;
            }

            return output;
        }
    }
}
