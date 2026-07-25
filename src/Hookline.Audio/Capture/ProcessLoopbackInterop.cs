using System.Runtime.InteropServices;
using NAudio.Wasapi.CoreAudioApi.Interfaces;

namespace Hookline.Audio.Capture;

internal static class ProcessLoopbackInterop
{
    private const string ProcessLoopbackDevice =
        "VAD\\Process_Loopback";
    private const ushort VariantBlob = 65;
    private static readonly Guid AudioClientInterfaceId =
        new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    private static readonly Guid CompletionHandlerInterfaceId =
        new("41D949AB-9862-444A-80F6-C261334DA5EB");

    public static async Task<ActivatedAudioClient> ActivateAsync(
        int processId,
        CancellationToken cancellationToken
    )
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
        {
            throw new PlatformNotSupportedException(
                AudioStrings.ProcessLoopbackUnsupported
            );
        }

        var activationParams = new AudioClientActivationParams
        {
            ActivationType = AudioClientActivationType.ProcessLoopback,
            ProcessLoopbackParams =
                new AudioClientProcessLoopbackParams
                {
                    TargetProcessId = checked((uint)processId),
                    Mode = ProcessLoopbackMode.IncludeTargetProcessTree,
                },
        };
        var paramsSize =
            Marshal.SizeOf<AudioClientActivationParams>();
        var paramsPointer = Marshal.AllocHGlobal(paramsSize);
        var variantPointer = Marshal.AllocHGlobal(
            Marshal.SizeOf<PropVariantBlobHeader>()
        );
        var handler = new ActivationCompletionHandler();
        IntPtr handlerPointer = IntPtr.Zero;
        IntPtr unknownPointer = IntPtr.Zero;
        try
        {
            Marshal.StructureToPtr(
                activationParams,
                paramsPointer,
                false
            );
            Marshal.StructureToPtr(
                new PropVariantBlobHeader
                {
                    VariantType = VariantBlob,
                    BlobSize = checked((uint)paramsSize),
                    BlobData = paramsPointer,
                },
                variantPointer,
                false
            );

            unknownPointer = Marshal.GetIUnknownForObject(handler);
            Marshal.ThrowExceptionForHR(
                Marshal.QueryInterface(
                    unknownPointer,
                    in CompletionHandlerInterfaceId,
                    out handlerPointer
                )
            );
            Marshal.ThrowExceptionForHR(
                ActivateAudioInterfaceAsync(
                    ProcessLoopbackDevice,
                    in AudioClientInterfaceId,
                    variantPointer,
                    handlerPointer,
                    out var operationPointer
                )
            );
            if (operationPointer != IntPtr.Zero)
            {
                Marshal.Release(operationPointer);
            }

            return await handler.Completion
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (handlerPointer != IntPtr.Zero)
            {
                Marshal.Release(handlerPointer);
            }

            if (unknownPointer != IntPtr.Zero)
            {
                Marshal.Release(unknownPointer);
            }

            Marshal.FreeHGlobal(variantPointer);
            Marshal.FreeHGlobal(paramsPointer);
            GC.KeepAlive(handler);
        }
    }

    [DllImport(
        "Mmdevapi.dll",
        CharSet = CharSet.Unicode,
        ExactSpelling = true
    )]
    private static extern int ActivateAudioInterfaceAsync(
        string deviceInterfacePath,
        in Guid interfaceId,
        IntPtr activationParams,
        IntPtr completionHandler,
        out IntPtr activationOperation
    );

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class ActivationCompletionHandler :
        IActivateAudioInterfaceCompletionHandler,
        IComAgileObject
    {
        private readonly TaskCompletionSource<ActivatedAudioClient> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ActivatedAudioClient> Completion => _completion.Task;

        public void ActivateCompleted(
            IActivateAudioInterfaceAsyncOperation operation
        )
        {
            try
            {
                var rawOperation =
                    (IRawActivateAudioInterfaceAsyncOperation)
                        operation;
                Marshal.ThrowExceptionForHR(
                    rawOperation.GetActivateResult(
                        out var activationResult,
                        out var audioClientPointer
                    )
                );
                Marshal.ThrowExceptionForHR(activationResult);
                _completion.TrySetResult(
                    new ActivatedAudioClient(audioClientPointer)
                );
            }
            catch (Exception exception)
            {
                _completion.TrySetException(exception);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioClientActivationParams
    {
        public AudioClientActivationType ActivationType;
        public AudioClientProcessLoopbackParams ProcessLoopbackParams;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioClientProcessLoopbackParams
    {
        public uint TargetProcessId;
        public ProcessLoopbackMode Mode;
    }

    private enum AudioClientActivationType
    {
        Default,
        ProcessLoopback,
    }

    private enum ProcessLoopbackMode
    {
        IncludeTargetProcessTree,
        ExcludeTargetProcessTree,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariantBlobHeader
    {
        public ushort VariantType;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public uint BlobSize;
        public IntPtr BlobData;
    }
}

internal sealed record ActivatedAudioClient(
    IntPtr OwnedPointer
);

[ComVisible(true)]
[Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IComAgileObject
{
}

[ComImport]
[Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IRawActivateAudioInterfaceAsyncOperation
{
    [PreserveSig]
    int GetActivateResult(
        out int activationResult,
        out IntPtr activatedInterface
    );
}
