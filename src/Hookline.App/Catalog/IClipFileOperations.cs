namespace Hookline.App.Catalog;

public interface IClipFileOperations
{
    bool Exists(string path);

    void Move(string sourcePath, string destinationPath);

    void Delete(string path);
}
