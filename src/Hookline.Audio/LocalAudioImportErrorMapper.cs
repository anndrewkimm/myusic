using System.Runtime.InteropServices;

namespace Hookline.Audio;

internal static class LocalAudioImportErrorMapper
{
    private const int MediaFoundationInvalidStreamNumber =
        unchecked((int)0xC00D36B3);

    public static LocalAudioImportException MapDecodeFailure(
        Exception exception
    )
    {
        ArgumentNullException.ThrowIfNull(exception);
        var message =
            exception is COMException
            {
                HResult: MediaFoundationInvalidStreamNumber,
            }
                ? AudioStrings.ImportHasNoAudio
                : AudioStrings.ImportDecodeFailed;
        return new LocalAudioImportException(
            LocalAudioImportFailure.DecodeFailed,
            message,
            exception
        );
    }
}
