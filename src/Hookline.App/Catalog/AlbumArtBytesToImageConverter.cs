using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Hookline.App.Catalog;

public sealed class AlbumArtBytesToImageConverter : IValueConverter
{
    public object? Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture
    )
    {
        if (value is not byte[] { Length: > 0 } bytes)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(
                bytes,
                writable: false
            );
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.DecodePixelWidth = 96;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception)
            when (exception is IOException
                or InvalidOperationException
                or NotSupportedException)
        {
            return null;
        }
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}
