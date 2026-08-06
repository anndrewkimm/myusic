using Hookline.Audio;

namespace Hookline.App.Mixing;

internal enum MixStemRole
{
    FullMix,
    VocalsOnly,
    InstrumentalOnly,
}

internal sealed record MixSourceEditorIntegration(
    Func<CancellationToken, Task<AudioBufferSnapshot?>> RenderAsync,
    Func<bool> CanRender,
    Func<CancellationToken, Task<bool>> EnsureStemsAsync,
    Func<bool> HasSeparatedStems,
    Func<double> GetStemProgressPercent,
    Action<MixStemRole> ApplyStemRole
);
