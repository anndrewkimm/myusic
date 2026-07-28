using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Hookline.App.Catalog;

public sealed class ClipCatalogItemViewModel
    : INotifyPropertyChanged
{
    private ClipCatalogEntry _entry;
    private string _editTitle;
    private bool _isEditing;
    private bool _isPlaying;

    public ClipCatalogItemViewModel(ClipCatalogEntry entry)
    {
        _entry =
            entry
            ?? throw new ArgumentNullException(nameof(entry));
        _editTitle = entry.DisplayTitle;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ClipCatalogEntry Entry => _entry;

    public Guid Id => _entry.Id;

    public string DisplayTitle => _entry.DisplayTitle;

    public string Artist =>
        string.IsNullOrWhiteSpace(_entry.SourceArtist)
            ? AppStrings.ArtistFallback
            : _entry.SourceArtist;

    public string Album => _entry.SourceAlbum;

    public byte[] AlbumArt => _entry.AlbumArt;

    public string FilePath => _entry.FilePath;

    public string DetailText => string.Format(
        CultureInfo.CurrentCulture,
        "{0:g}  ·  {1}–{2}  ·  {3:0.0}s",
        _entry.ExportedAt.ToLocalTime(),
        FormatPosition(_entry.TrimStart),
        FormatPosition(_entry.TrimEnd),
        _entry.Duration.TotalSeconds
    );

    public bool IsMissing => _entry.IsMissing;

    public string MissingText =>
        IsMissing ? AppStrings.CatalogMissing : string.Empty;

    public bool CanUseFile => !IsMissing;

    public string EditTitle
    {
        get => _editTitle;
        set
        {
            if (_editTitle == value)
            {
                return;
            }

            _editTitle = value;
            OnPropertyChanged();
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        private set
        {
            if (_isEditing == value)
            {
                return;
            }

            _isEditing = value;
            OnPropertyChanged();
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying == value)
            {
                return;
            }

            _isPlaying = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlayButtonText));
        }
    }

    public string PlayButtonText =>
        IsPlaying ? AppStrings.CatalogStop : AppStrings.CatalogPlay;

    public void BeginEdit()
    {
        EditTitle = DisplayTitle;
        IsEditing = true;
    }

    public void CancelEdit()
    {
        EditTitle = DisplayTitle;
        IsEditing = false;
    }

    public void FinishEdit() => IsEditing = false;

    public void Update(ClipCatalogEntry entry)
    {
        _entry =
            entry
            ?? throw new ArgumentNullException(nameof(entry));
        if (!IsEditing)
        {
            _editTitle = entry.DisplayTitle;
        }

        OnPropertyChanged(nameof(Entry));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(Artist));
        OnPropertyChanged(nameof(Album));
        OnPropertyChanged(nameof(AlbumArt));
        OnPropertyChanged(nameof(FilePath));
        OnPropertyChanged(nameof(DetailText));
        OnPropertyChanged(nameof(IsMissing));
        OnPropertyChanged(nameof(MissingText));
        OnPropertyChanged(nameof(CanUseFile));
        OnPropertyChanged(nameof(EditTitle));
    }

    private static string FormatPosition(TimeSpan position) =>
        string.Create(
            CultureInfo.CurrentCulture,
            $"{(int)position.TotalMinutes}:{position.Seconds:00}.{position.Milliseconds / 100}"
        );

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null
    ) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName)
        );
}
