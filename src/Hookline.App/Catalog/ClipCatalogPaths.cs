using System.IO;

namespace Hookline.App.Catalog;

public static class ClipCatalogPaths
{
    public static string GetDefaultDatabasePath()
    {
        var localData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData
        );
        return Path.Combine(
            localData,
            AppStrings.AppName,
            "catalog.db"
        );
    }
}
