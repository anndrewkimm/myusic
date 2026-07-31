using System.Globalization;
using Hookline.App.Catalog;

namespace Hookline.App.Mixing;

public sealed record MixCatalogSource
{
    public required ClipCatalogEntry Entry { get; init; }

    public string Label
    {
        get
        {
            var artist = string.IsNullOrWhiteSpace(
                Entry.SourceArtist
            )
                ? AppStrings.ArtistFallback
                : Entry.SourceArtist;
            return string.Format(
                CultureInfo.CurrentCulture,
                "{0} — {1} ({2:0.0}s){3}",
                Entry.DisplayTitle,
                artist,
                Entry.Duration.TotalSeconds,
                Entry.IsMissing
                    ? $" — {AppStrings.CatalogMissing}"
                    : string.Empty
            );
        }
    }

    public bool CanSelect => !Entry.IsMissing;
}
