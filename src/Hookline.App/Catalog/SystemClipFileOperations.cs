using System.IO;

namespace Hookline.App.Catalog;

public sealed class SystemClipFileOperations : IClipFileOperations
{
    public bool Exists(string path) => File.Exists(path);

    public void Move(string sourcePath, string destinationPath) =>
        File.Move(sourcePath, destinationPath);

    public void Delete(string path) => File.Delete(path);
}
