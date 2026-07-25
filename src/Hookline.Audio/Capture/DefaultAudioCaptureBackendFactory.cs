namespace Hookline.Audio.Capture;

public sealed class DefaultAudioCaptureBackendFactory :
    IAudioCaptureBackendFactory
{
    private readonly PcmAudioFormat _format;

    public DefaultAudioCaptureBackendFactory(
        PcmAudioFormat? format = null
    )
    {
        _format = format ?? new PcmAudioFormat(44_100, 16, 2);
    }

    public async Task<AudioCaptureBackendSelection> CreateAsync(
        int? targetProcessId,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        Exception? processCaptureFailure = null;
        if (targetProcessId is > 0)
        {
            try
            {
                var backend =
                    await ProcessLoopbackAudioCaptureBackend.CreateAsync(
                            targetProcessId.Value,
                            _format,
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                return new AudioCaptureBackendSelection
                {
                    Backend = backend,
                };
            }
            catch (Exception exception)
                when (
                    exception is not OperationCanceledException
                    || !cancellationToken.IsCancellationRequested
                )
            {
                processCaptureFailure = exception;
            }
        }

        var fallback = new SystemLoopbackAudioCaptureBackend(_format);
        var reason =
            processCaptureFailure is null
                ? AudioStrings.NoTargetProcess
                : $"{AudioStrings.ProcessCaptureUnavailable} "
                    + processCaptureFailure.Message;
        return new AudioCaptureBackendSelection
        {
            Backend = fallback,
            FallbackReason = reason,
        };
    }
}
