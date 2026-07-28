namespace Hookline.Audio;

public sealed class LocalAudioImportException : Exception
{
    public LocalAudioImportException(
        LocalAudioImportFailure failure,
        string message,
        Exception? innerException = null
    )
        : base(message, innerException)
    {
        Failure = failure;
    }

    public LocalAudioImportFailure Failure { get; }
}
