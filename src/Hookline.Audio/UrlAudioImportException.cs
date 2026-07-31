namespace Hookline.Audio;

public sealed class UrlAudioImportException : Exception
{
    public UrlAudioImportException(
        UrlAudioImportFailure failure,
        string message,
        Exception? innerException = null
    )
        : base(message, innerException)
    {
        Failure = failure;
    }

    public UrlAudioImportFailure Failure { get; }
}
