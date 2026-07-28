namespace Hookline.Audio;

public sealed class Mp3TagEditor
{
    public void UpdateTitle(string filePath, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        using var file = TagLib.File.Create(filePath);
        file.RemoveTags(TagLib.TagTypes.Id3v1);
        var tag = (TagLib.Id3v2.Tag)file.GetTag(
            TagLib.TagTypes.Id3v2,
            create: true
        );
        tag.Version = 4;
        tag.Title = title.Trim();
        file.Save();
    }
}
