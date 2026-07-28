using System.Buffers.Binary;
using Microsoft.ML.OnnxRuntime;

namespace Hookline.Audio;

public sealed class OnnxStemSeparator
{
    public const int ModelSampleRate = 44_100;
    public const int SegmentFrameCount = 343_980;
    public static readonly TimeSpan MaximumSelectionDuration =
        TimeSpan.FromMinutes(5);

    private const int ChannelCount = 2;
    private const int OverlapFrameCount =
        SegmentFrameCount / 4;
    private const int StrideFrameCount =
        SegmentFrameCount - OverlapFrameCount;
    private static readonly long[] InputShape =
        [1, ChannelCount, SegmentFrameCount];

    public Task<SeparatedStemSet> SeparateAsync(
        AudioBufferSnapshot selection,
        string modelPath,
        StemModelDescriptor descriptor,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentNullException.ThrowIfNull(descriptor);
        Validate(selection);
        return Task.Run(
            () =>
                SeparateCore(
                    selection,
                    modelPath,
                    descriptor,
                    progress,
                    cancellationToken
                ),
            cancellationToken
        );
    }

    private static SeparatedStemSet SeparateCore(
        AudioBufferSnapshot selection,
        string modelPath,
        StemModelDescriptor descriptor,
        IProgress<double>? progress,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(0);
        using var sessionOptions = new SessionOptions
        {
            GraphOptimizationLevel =
                GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
        using var session = new InferenceSession(
            modelPath,
            sessionOptions
        );
        cancellationToken.ThrowIfCancellationRequested();

        if (
            !session.InputNames.Contains(
                "mix",
                StringComparer.Ordinal
            )
            || !session.OutputNames.Contains(
                "stems",
                StringComparer.Ordinal
            )
        )
        {
            throw new InvalidDataException(
                AudioStrings.InvalidStemOutput
            );
        }

        progress?.Report(0.05);
        var format = selection.Format;
        var totalFrames =
            selection.Audio.Length / format.BlockAlign;
        var chunkCount = Math.Max(
            1,
            (totalFrames + StrideFrameCount - 1)
                / StrideFrameCount
        );
        var stemCount = descriptor.Stems.Count;
        var outputs = Enumerable
            .Range(0, stemCount)
            .Select(
                _ =>
                    new byte[
                        checked(totalFrames * format.BlockAlign)
                    ]
            )
            .ToArray();
        var input = new float[
            ChannelCount * SegmentFrameCount
        ];
        var previousTail = new float[
            stemCount
                * ChannelCount
                * OverlapFrameCount
        ];
        var previousTailLength = 0;
        var sourceAudio = selection.Audio.Span;

        for (
            var chunkIndex = 0;
            chunkIndex < chunkCount;
            chunkIndex++
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Array.Clear(input);
            var startFrame =
                chunkIndex * StrideFrameCount;
            var chunkLength = Math.Min(
                SegmentFrameCount,
                totalFrames - startFrame
            );
            FillInput(
                sourceAudio,
                format,
                startFrame,
                chunkLength,
                input
            );

            using var inputValue =
                OrtValue.CreateTensorValueFromMemory(
                    input,
                    InputShape
                );
            var inputs = new Dictionary<string, OrtValue>
            {
                ["mix"] = inputValue,
            };
            using var runOptions = new RunOptions();
            using var cancellationRegistration =
                cancellationToken.Register(
                    () => runOptions.Terminate = true
                );
            try
            {
                using var inferenceOutputs = session.Run(
                    runOptions,
                    inputs,
                    ["stems"]
                );
                if (inferenceOutputs.Count != 1)
                {
                    throw new InvalidDataException(
                        AudioStrings.InvalidStemOutput
                    );
                }

                var modelOutput =
                    inferenceOutputs[0]
                        .GetTensorDataAsSpan<float>();
                var expectedOutputLength = checked(
                    stemCount
                        * ChannelCount
                        * SegmentFrameCount
                );
                if (modelOutput.Length != expectedOutputLength)
                {
                    throw new InvalidDataException(
                        AudioStrings.InvalidStemOutput
                    );
                }

                WriteChunk(
                    modelOutput,
                    outputs,
                    previousTail,
                    ref previousTailLength,
                    chunkIndex,
                    chunkCount,
                    startFrame,
                    chunkLength,
                    format
                );
            }
            catch (OnnxRuntimeException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(
                    cancellationToken
                );
            }

            progress?.Report(
                0.05
                    + (
                        0.95
                        * (chunkIndex + 1d)
                        / chunkCount
                    )
            );
        }

        var stems = descriptor.Stems
            .Select(
                (kind, index) =>
                    new SeparatedStem
                    {
                        Kind = kind,
                        Snapshot = selection with
                        {
                            Audio = outputs[index],
                        },
                    }
            )
            .ToArray();
        return new SeparatedStemSet
        {
            Mode = descriptor.Mode,
            Source = selection,
            Stems = stems,
        };
    }

    private static void FillInput(
        ReadOnlySpan<byte> source,
        PcmAudioFormat format,
        int startFrame,
        int frameCount,
        Span<float> input
    )
    {
        for (var frame = 0; frame < frameCount; frame++)
        {
            var sourceOffset =
                (startFrame + frame) * format.BlockAlign;
            for (
                var channel = 0;
                channel < ChannelCount;
                channel++
            )
            {
                var sample =
                    BinaryPrimitives.ReadInt16LittleEndian(
                        source.Slice(
                            sourceOffset
                                + (channel * sizeof(short)),
                            sizeof(short)
                        )
                    );
                input[
                    (channel * SegmentFrameCount) + frame
                ] = sample / 32768f;
            }
        }
    }

    private static void WriteChunk(
        ReadOnlySpan<float> modelOutput,
        IReadOnlyList<byte[]> outputs,
        Span<float> previousTail,
        ref int previousTailLength,
        int chunkIndex,
        int chunkCount,
        int startFrame,
        int chunkLength,
        PcmAudioFormat format
    )
    {
        var isFinal = chunkIndex == chunkCount - 1;
        var outputLimit = isFinal
            ? chunkLength
            : Math.Min(StrideFrameCount, chunkLength);
        var overlapLength =
            chunkIndex == 0
                ? 0
                : Math.Min(
                    previousTailLength,
                    Math.Min(
                        OverlapFrameCount,
                        chunkLength
                    )
                );

        for (var stem = 0; stem < outputs.Count; stem++)
        {
            for (
                var channel = 0;
                channel < ChannelCount;
                channel++
            )
            {
                var modelChannelOffset =
                    (
                        (stem * ChannelCount)
                        + channel
                    )
                    * SegmentFrameCount;
                var tailChannelOffset =
                    (
                        (stem * ChannelCount)
                        + channel
                    )
                    * OverlapFrameCount;
                for (
                    var frame = 0;
                    frame < overlapLength;
                    frame++
                )
                {
                    var incomingWeight =
                        frame
                        / (double)(
                            OverlapFrameCount - 1
                        );
                    var outgoingWeight =
                        1d - incomingWeight;
                    var sample =
                        (
                            previousTail[
                                tailChannelOffset + frame
                            ]
                            * outgoingWeight
                        )
                        + (
                            modelOutput[
                                modelChannelOffset + frame
                            ]
                            * incomingWeight
                        );
                    WriteSample(
                        outputs[stem],
                        startFrame + frame,
                        channel,
                        format,
                        sample
                    );
                }

                for (
                    var frame = overlapLength;
                    frame < outputLimit;
                    frame++
                )
                {
                    WriteSample(
                        outputs[stem],
                        startFrame + frame,
                        channel,
                        format,
                        modelOutput[
                            modelChannelOffset + frame
                        ]
                    );
                }
            }
        }

        if (isFinal)
        {
            previousTailLength = 0;
            return;
        }

        previousTailLength = Math.Max(
            0,
            chunkLength - StrideFrameCount
        );
        for (var stem = 0; stem < outputs.Count; stem++)
        {
            for (
                var channel = 0;
                channel < ChannelCount;
                channel++
            )
            {
                var modelChannelOffset =
                    (
                        (stem * ChannelCount)
                        + channel
                    )
                    * SegmentFrameCount;
                var tailChannelOffset =
                    (
                        (stem * ChannelCount)
                        + channel
                    )
                    * OverlapFrameCount;
                modelOutput.Slice(
                    modelChannelOffset + StrideFrameCount,
                    previousTailLength
                ).CopyTo(
                    previousTail.Slice(
                        tailChannelOffset,
                        previousTailLength
                    )
                );
            }
        }
    }

    private static void WriteSample(
        Span<byte> output,
        int frame,
        int channel,
        PcmAudioFormat format,
        double normalizedSample
    )
    {
        var scaled = Math.Round(normalizedSample * 32768d);
        var sample = (short)Math.Clamp(
            scaled,
            short.MinValue,
            short.MaxValue
        );
        var offset =
            (frame * format.BlockAlign)
            + (channel * sizeof(short));
        BinaryPrimitives.WriteInt16LittleEndian(
            output.Slice(offset, sizeof(short)),
            sample
        );
    }

    private static void Validate(AudioBufferSnapshot selection)
    {
        var format = selection.Format;
        if (
            format.SampleRate != ModelSampleRate
            || format.BitsPerSample != 16
            || format.Channels != ChannelCount
        )
        {
            throw new NotSupportedException(
                AudioStrings.UnsupportedStemFormat
            );
        }

        if (selection.Duration > MaximumSelectionDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(selection),
                AudioStrings.StemSelectionTooLong
            );
        }

        if (selection.Audio.IsEmpty)
        {
            throw new ArgumentException(
                AudioStrings.EmptyExport,
                nameof(selection)
            );
        }
    }
}
