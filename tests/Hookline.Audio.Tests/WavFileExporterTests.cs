namespace Hookline.Audio.Tests;

public sealed class WavFileExporterTests
{
    [Fact]
    public async Task WritesPcmWaveHeaderAndSnapshotData()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"hookline-{Guid.NewGuid():N}.wav"
        );
        try
        {
            var audio = new byte[] { 1, 2, 3, 4 };
            var snapshot = new AudioBufferSnapshot
            {
                TrackInstanceId = 1,
                Format = new PcmAudioFormat(44_100, 16, 2),
                Audio = audio,
                RequestedStart = TimeSpan.Zero,
                RequestedEnd = TimeSpan.FromTicks(1),
            };

            await WavFileExporter.WriteAsync(path, snapshot);

            var file = await File.ReadAllBytesAsync(path);
            Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(file, 0, 4));
            Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(file, 8, 4));
            Assert.Equal("data", System.Text.Encoding.ASCII.GetString(file, 36, 4));
            Assert.Equal(audio, file[44..]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
