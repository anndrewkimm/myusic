using System.Runtime.InteropServices;

namespace Hookline.Audio.Capture;

internal sealed class ProcessLoopbackAudioCaptureBackend :
    IAudioCaptureBackend
{
    private const int SharedMode = 0;
    private const uint Loopback = 0x00020000;
    private const uint EventCallback = 0x00040000;
    private const uint SourceDefaultQuality = 0x08000000;
    private const uint AutoConvertPcm = 0x80000000;
    private const uint SilentBuffer = 0x00000002;
    private static readonly Guid AudioCaptureClientInterfaceId =
        new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");

    private readonly object _gate = new();
    private readonly AutoResetEvent _samplesReady = new(false);
    private readonly IntPtr _audioClientPointer;
    private readonly IntPtr _captureClientPointer;

    private CancellationTokenSource? _captureCancellation;
    private Task? _captureTask;
    private bool _started;
    private bool _disposed;

    private ProcessLoopbackAudioCaptureBackend(
        PcmAudioFormat format,
        IntPtr audioClientPointer,
        IntPtr captureClientPointer
    )
    {
        Format = format;
        _audioClientPointer = audioClientPointer;
        _captureClientPointer = captureClientPointer;
    }

    public event EventHandler<AudioDataAvailableEventArgs>? DataAvailable;

    public event EventHandler<AudioBackendStoppedEventArgs>? Stopped;

    public AudioCaptureMode Mode => AudioCaptureMode.ProcessLoopback;

    public PcmAudioFormat Format { get; }

    public static async Task<ProcessLoopbackAudioCaptureBackend>
        CreateAsync(
            int processId,
            PcmAudioFormat format,
            CancellationToken cancellationToken
        )
    {
        var activatedAudioClient = await ProcessLoopbackInterop
            .ActivateAsync(processId, cancellationToken)
            .ConfigureAwait(false);
        IntPtr formatPointer = IntPtr.Zero;
        IntPtr captureClientPointer = IntPtr.Zero;
        var succeeded = false;
        try
        {
            var waveFormat = new WaveFormatEx
            {
                FormatTag = 1,
                Channels = checked((ushort)format.Channels),
                SamplesPerSecond = checked((uint)format.SampleRate),
                AverageBytesPerSecond = checked(
                    (uint)format.AverageBytesPerSecond
                ),
                BlockAlign = checked((ushort)format.BlockAlign),
                BitsPerSample = checked(
                    (ushort)format.BitsPerSample
                ),
            };
            formatPointer = Marshal.AllocHGlobal(
                Marshal.SizeOf<WaveFormatEx>()
            );
            Marshal.StructureToPtr(waveFormat, formatPointer, false);

            Marshal.ThrowExceptionForHR(
                ComAudioClient.Initialize(
                    activatedAudioClient.OwnedPointer,
                    SharedMode,
                    Loopback
                        | EventCallback
                        | AutoConvertPcm
                        | SourceDefaultQuality,
                    0,
                    0,
                    formatPointer,
                    IntPtr.Zero
                )
            );
            Marshal.ThrowExceptionForHR(
                ComAudioClient.GetService(
                    activatedAudioClient.OwnedPointer,
                    in AudioCaptureClientInterfaceId,
                    out captureClientPointer
                )
            );
            var backend = new ProcessLoopbackAudioCaptureBackend(
                format,
                activatedAudioClient.OwnedPointer,
                captureClientPointer
            );
            succeeded = true;
            return backend;
        }
        catch
        {
            Marshal.Release(activatedAudioClient.OwnedPointer);
            throw;
        }
        finally
        {
            if (
                !succeeded
                && captureClientPointer != IntPtr.Zero
            )
            {
                Marshal.Release(captureClientPointer);
            }

            if (formatPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(formatPointer);
            }
        }
    }

    public Task StartAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            if (_started)
            {
                return Task.CompletedTask;
            }

            Marshal.ThrowExceptionForHR(
                ComAudioClient.SetEventHandle(
                    _audioClientPointer,
                    _samplesReady.SafeWaitHandle.DangerousGetHandle()
                )
            );
            Marshal.ThrowExceptionForHR(
                ComAudioClient.Start(_audioClientPointer)
            );
            _captureCancellation = new CancellationTokenSource();
            _started = true;
            _captureTask = Task.Run(
                () => CaptureLoop(_captureCancellation.Token),
                CancellationToken.None
            );
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(
        CancellationToken cancellationToken = default
    )
    {
        Task? captureTask;
        var audioClientWasStarted = false;
        lock (_gate)
        {
            if (!_started && _captureTask is null)
            {
                return;
            }

            audioClientWasStarted = _started;
            _started = false;
            _captureCancellation?.Cancel();
            _samplesReady.Set();
            captureTask = _captureTask;
        }

        if (audioClientWasStarted)
        {
            Marshal.ThrowExceptionForHR(
                ComAudioClient.Stop(_audioClientPointer)
            );
        }
        if (captureTask is not null)
        {
            await captureTask
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        lock (_gate)
        {
            _captureCancellation?.Dispose();
            _captureCancellation = null;
            _captureTask = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _samplesReady.Dispose();
        Marshal.Release(_captureClientPointer);
        Marshal.Release(_audioClientPointer);

        _disposed = true;
    }

    private void CaptureLoop(CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            var waitHandles = new[]
            {
                _samplesReady,
                cancellationToken.WaitHandle,
            };
            while (!cancellationToken.IsCancellationRequested)
            {
                if (WaitHandle.WaitAny(waitHandles) != 0)
                {
                    break;
                }

                DrainPackets();
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            if (failure is not null)
            {
                lock (_gate)
                {
                    _started = false;
                }

                Stopped?.Invoke(
                    this,
                    new AudioBackendStoppedEventArgs(failure)
                );
            }
        }
    }

    private void DrainPackets()
    {
        Marshal.ThrowExceptionForHR(
            ComAudioCaptureClient.GetNextPacketSize(
                _captureClientPointer,
                out var frames
            )
        );
        while (frames > 0)
        {
            IntPtr data = IntPtr.Zero;
            uint acquiredFrames = 0;
            try
            {
                Marshal.ThrowExceptionForHR(
                    ComAudioCaptureClient.GetBuffer(
                        _captureClientPointer,
                        out data,
                        out acquiredFrames,
                        out var flags,
                        out _,
                        out _
                    )
                );
                var byteCount = checked(
                    (int)acquiredFrames * Format.BlockAlign
                );
                var copy = new byte[byteCount];
                if (
                    byteCount > 0
                    && (flags & SilentBuffer) == 0
                )
                {
                    Marshal.Copy(data, copy, 0, byteCount);
                }

                if (copy.Length > 0)
                {
                    DataAvailable?.Invoke(
                        this,
                        new AudioDataAvailableEventArgs(
                            copy,
                            DateTimeOffset.UtcNow
                        )
                    );
                }
            }
            finally
            {
                if (acquiredFrames > 0)
                {
                    Marshal.ThrowExceptionForHR(
                        ComAudioCaptureClient.ReleaseBuffer(
                            _captureClientPointer,
                            acquiredFrames
                        )
                    );
                }
            }

            Marshal.ThrowExceptionForHR(
                ComAudioCaptureClient.GetNextPacketSize(
                    _captureClientPointer,
                    out frames
                )
            );
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormatEx
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSecond;
        public uint AverageBytesPerSecond;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;
    }

    private static class ComAudioClient
    {
        public static int Initialize(
            IntPtr instance,
            int shareMode,
            uint streamFlags,
            long bufferDuration,
            long periodicity,
            IntPtr format,
            IntPtr audioSessionGuid
        ) =>
            GetMethod<InitializeDelegate>(instance, 3)(
                instance,
                shareMode,
                streamFlags,
                bufferDuration,
                periodicity,
                format,
                audioSessionGuid
            );

        public static int Start(IntPtr instance) =>
            GetMethod<NoArgumentsDelegate>(instance, 10)(instance);

        public static int Stop(IntPtr instance) =>
            GetMethod<NoArgumentsDelegate>(instance, 11)(instance);

        public static int SetEventHandle(
            IntPtr instance,
            IntPtr eventHandle
        ) =>
            GetMethod<SetEventHandleDelegate>(instance, 13)(
                instance,
                eventHandle
            );

        public static int GetService(
            IntPtr instance,
            in Guid interfaceId,
            out IntPtr service
        ) =>
            GetMethod<GetServiceDelegate>(instance, 14)(
                instance,
                in interfaceId,
                out service
            );

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int InitializeDelegate(
            IntPtr instance,
            int shareMode,
            uint streamFlags,
            long bufferDuration,
            long periodicity,
            IntPtr format,
            IntPtr audioSessionGuid
        );

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int NoArgumentsDelegate(IntPtr instance);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetEventHandleDelegate(
            IntPtr instance,
            IntPtr eventHandle
        );

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetServiceDelegate(
            IntPtr instance,
            in Guid interfaceId,
            out IntPtr service
        );
    }

    private static class ComAudioCaptureClient
    {
        public static int GetBuffer(
            IntPtr instance,
            out IntPtr data,
            out uint frames,
            out uint flags,
            out ulong devicePosition,
            out ulong performanceCounterPosition
        ) =>
            GetMethod<GetBufferDelegate>(instance, 3)(
                instance,
                out data,
                out frames,
                out flags,
                out devicePosition,
                out performanceCounterPosition
            );

        public static int ReleaseBuffer(
            IntPtr instance,
            uint frames
        ) =>
            GetMethod<ReleaseBufferDelegate>(instance, 4)(
                instance,
                frames
            );

        public static int GetNextPacketSize(
            IntPtr instance,
            out uint frames
        ) =>
            GetMethod<GetNextPacketSizeDelegate>(instance, 5)(
                instance,
                out frames
            );

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetBufferDelegate(
            IntPtr instance,
            out IntPtr data,
            out uint frames,
            out uint flags,
            out ulong devicePosition,
            out ulong performanceCounterPosition
        );

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ReleaseBufferDelegate(
            IntPtr instance,
            uint frames
        );

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetNextPacketSizeDelegate(
            IntPtr instance,
            out uint frames
        );
    }

    private static TDelegate GetMethod<TDelegate>(
        IntPtr instance,
        int slot
    )
        where TDelegate : Delegate
    {
        var vtable = Marshal.ReadIntPtr(instance);
        var method = Marshal.ReadIntPtr(
            vtable,
            slot * IntPtr.Size
        );
        return Marshal.GetDelegateForFunctionPointer<TDelegate>(
            method
        );
    }
}
