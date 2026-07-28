namespace Hookline.Audio;

public static class StemModelCatalog
{
    public static StemModelDescriptor FourStem { get; } =
        new()
        {
            Mode = StemSeparationMode.FourStem,
            DisplayName = "HT-Demucs 4-stem",
            FileName = "htdemucs_fp16weights.onnx",
            DownloadUri = new Uri(
                "https://huggingface.co/StemSplitio/htdemucs-onnx/resolve/main/htdemucs_fp16weights.onnx?download=true"
            ),
            DisplayDownloadSize = "166 MB",
            Sha256 =
                "d05c269d0178d2a72ad484b10b11dd370193fc923201c3b27a99f848745db70a",
            Stems =
            [
                StemKind.Drums,
                StemKind.Bass,
                StemKind.Other,
                StemKind.Vocals,
            ],
        };

    public static StemModelDescriptor SixStemExperimental { get; } =
        new()
        {
            Mode = StemSeparationMode.SixStemExperimental,
            DisplayName = "HT-Demucs 6-stem",
            FileName = "htdemucs_6s_fp16weights.onnx",
            DownloadUri = new Uri(
                "https://huggingface.co/StemSplitio/htdemucs-6s-onnx/resolve/main/htdemucs_6s_fp16weights.onnx?download=true"
            ),
            DisplayDownloadSize = "136 MB",
            Sha256 =
                "7ce55792e2231c93fbf92de95f5fd5b3a5e6c89f7db690dfd693e8f1dce56869",
            Stems =
            [
                StemKind.Drums,
                StemKind.Bass,
                StemKind.Other,
                StemKind.Vocals,
                StemKind.Guitar,
                StemKind.Piano,
            ],
        };

    public static StemModelDescriptor Get(
        StemSeparationMode mode
    ) =>
        mode switch
        {
            StemSeparationMode.FourStem => FourStem,
            StemSeparationMode.SixStemExperimental =>
                SixStemExperimental,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
}
