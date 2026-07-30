using System.Buffers.Binary;

namespace Hookline.Audio;

internal static class StereoRotationEffect
{
    public static byte[] Apply(
        ReadOnlySpan<byte> source,
        PcmAudioFormat format,
        double rateHertz,
        CancellationToken cancellationToken
    )
    {
        var alignedLength =
            source.Length - (source.Length % format.BlockAlign);
        var output = source[..alignedLength].ToArray();
        if (format.Channels < 2 || output.Length == 0)
        {
            return output;
        }

        var frameCount = output.Length / format.BlockAlign;
        var phaseIncrement =
            2 * Math.PI * rateHertz / format.SampleRate;
        var incrementSin = Math.Sin(phaseIncrement);
        var incrementCos = Math.Cos(phaseIncrement);
        var phaseSin = 0d;
        var phaseCos = 1d;

        for (var frame = 0; frame < frameCount; frame++)
        {
            if ((frame & 0x3FFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var left = ReadSample(
                source,
                frame,
                0,
                format
            );
            var right = ReadSample(
                source,
                frame,
                1,
                format
            );
            var moveGain = Math.Abs(phaseSin);
            var stayGain = Math.Sqrt(
                Math.Max(
                    0,
                    1 - (moveGain * moveGain)
                )
            );
            var combinedGain =
                1 / Math.Sqrt(1 + (moveGain * moveGain));
            var rotatedLeft =
                phaseSin >= 0
                    ? left * stayGain
                    : (
                        left + (right * moveGain)
                    ) * combinedGain;
            var rotatedRight =
                phaseSin >= 0
                    ? (
                        right + (left * moveGain)
                    ) * combinedGain
                    : right * stayGain;
            WriteSample(
                output,
                frame,
                0,
                format,
                rotatedLeft
            );
            WriteSample(
                output,
                frame,
                1,
                format,
                rotatedRight
            );

            var nextSin =
                (phaseSin * incrementCos)
                + (phaseCos * incrementSin);
            phaseCos =
                (phaseCos * incrementCos)
                - (phaseSin * incrementSin);
            phaseSin = nextSin;
            if ((frame & 0xFFF) == 0xFFF)
            {
                var magnitude = Math.Sqrt(
                    (phaseSin * phaseSin)
                    + (phaseCos * phaseCos)
                );
                phaseSin /= magnitude;
                phaseCos /= magnitude;
            }
        }

        return output;
    }

    private static short ReadSample(
        ReadOnlySpan<byte> audio,
        int frame,
        int channel,
        PcmAudioFormat format
    )
    {
        var offset =
            (frame * format.BlockAlign)
            + (channel * sizeof(short));
        return BinaryPrimitives.ReadInt16LittleEndian(
            audio.Slice(offset, sizeof(short))
        );
    }

    private static void WriteSample(
        Span<byte> audio,
        int frame,
        int channel,
        PcmAudioFormat format,
        double sample
    )
    {
        var offset =
            (frame * format.BlockAlign)
            + (channel * sizeof(short));
        BinaryPrimitives.WriteInt16LittleEndian(
            audio.Slice(offset, sizeof(short)),
            (short)Math.Clamp(
                Math.Round(sample),
                short.MinValue,
                short.MaxValue
            )
        );
    }
}
