using System.IO;

namespace Hookline.App.Catalog;

public sealed class ClipCatalogMissingFileException(string message)
    : IOException(message);
