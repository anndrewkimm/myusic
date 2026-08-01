using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using Hookline.App.Catalog;
using Hookline.Audio;

namespace Hookline.App.Mixing;

public sealed class MixWindowViewModel
    : INotifyPropertyChanged,
      IDisposable
{
    private readonly ClipCatalogService _catalog;
    private readonly LocalAudioFileImporter _importer;
    private readonly IClipExporter _exporter;
    private readonly OutputFolderSettings _outputSettings;
    private readonly CancellationTokenSource _lifetimeCancellation =
        new();
    private readonly Dictionary<string, ImportedAudioFile> _sourceCache =
        new(StringComparer.OrdinalIgnoreCase);
    private ImportedAudioFile? _firstSource;
    private ImportedAudioFile? _secondSource;
    private Func<
        CancellationToken,
        Task<AudioBufferSnapshot?>
    >? _firstRenderer;
    private Func<
        CancellationToken,
        Task<AudioBufferSnapshot?>
    >? _secondRenderer;
    private Func<bool>? _firstCanRender;
    private Func<bool>? _secondCanRender;
    private double _firstVolumePercent = 100;
    private double _secondVolumePercent = 100;
    private string _exportTitle = string.Empty;
    private string _exportArtist = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isLoadingSource;
    private bool _isExporting;
    private bool _disposed;

    public MixWindowViewModel(
        ClipCatalogService catalog,
        LocalAudioFileImporter importer,
        IClipExporter exporter,
        OutputFolderSettings outputSettings
    )
    {
        _catalog =
            catalog
            ?? throw new ArgumentNullException(nameof(catalog));
        _importer =
            importer
            ?? throw new ArgumentNullException(nameof(importer));
        _exporter =
            exporter
            ?? throw new ArgumentNullException(nameof(exporter));
        _outputSettings =
            outputSettings
            ?? throw new ArgumentNullException(
                nameof(outputSettings)
            );
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<MixSourceChangedEventArgs>?
        SourceChanged;

    public ObservableCollection<MixCatalogSource> CatalogSources
    {
        get;
    } = [];

    public bool HasFirstSource => _firstSource is not null;

    public bool HasSecondSource => _secondSource is not null;

    public string FirstSourceTitle => GetTitle(_firstSource);

    public string SecondSourceTitle => GetTitle(_secondSource);

    public string FirstSourceDetail => GetDetail(_firstSource);

    public string SecondSourceDetail => GetDetail(_secondSource);

    public double FirstVolumePercent
    {
        get => _firstVolumePercent;
        set
        {
            var normalized = NormalizeVolume(value);
            if (_firstVolumePercent == normalized)
            {
                return;
            }

            _firstVolumePercent = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FirstVolumeText));
        }
    }

    public double SecondVolumePercent
    {
        get => _secondVolumePercent;
        set
        {
            var normalized = NormalizeVolume(value);
            if (_secondVolumePercent == normalized)
            {
                return;
            }

            _secondVolumePercent = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SecondVolumeText));
        }
    }

    public string FirstVolumeText => FormatVolume(
        FirstVolumePercent
    );

    public string SecondVolumeText => FormatVolume(
        SecondVolumePercent
    );

    public string ExportTitle
    {
        get => _exportTitle;
        set
        {
            if (_exportTitle == value)
            {
                return;
            }

            _exportTitle = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanExport));
        }
    }

    public string ExportArtist
    {
        get => _exportArtist;
        set
        {
            if (_exportArtist == value)
            {
                return;
            }

            _exportArtist = value;
            OnPropertyChanged();
        }
    }

    public string OutputFolder => _outputSettings.OutputFolder;

    public string OutputFolderText => string.Format(
        CultureInfo.CurrentCulture,
        "{0}: {1}",
        AppStrings.MixOutputFolder,
        OutputFolder
    );

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value)
            {
                return;
            }

            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public bool IsBusy => _isLoadingSource || _isExporting;

    public bool CanChooseSources => !IsBusy;

    public bool CanExport =>
        HasFirstSource
        && HasSecondSource
        && !string.IsNullOrWhiteSpace(ExportTitle)
        && CanRenderSources
        && !IsBusy;

    public string ExportButtonText =>
        _isExporting
            ? AppStrings.MixExporting
            : AppStrings.MixExport;

    public async Task LoadCatalogAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            var entries = await _catalog.GetAllAsync(
                CatalogSortOrder.MostRecent,
                _lifetimeCancellation.Token
            );
            CatalogSources.Clear();
            foreach (var entry in entries)
            {
                CatalogSources.Add(
                    new MixCatalogSource { Entry = entry }
                );
            }

            if (CatalogSources.Count == 0)
            {
                StatusMessage = AppStrings.MixCatalogEmpty;
            }
        }
        catch (OperationCanceledException)
            when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusMessage = Format(
                AppStrings.MixCatalogLoadFailed,
                exception.Message
            );
        }
    }

    public Task LoadCatalogSourceAsync(
        MixSourceSlot slot,
        MixCatalogSource source
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanSelect)
        {
            StatusMessage = Format(
                AppStrings.CatalogFileMissing,
                source.Entry.FilePath
            );
            return Task.CompletedTask;
        }

        return LoadSourceAsync(slot, source.Entry.FilePath);
    }

    public Task ImportSourceAsync(
        MixSourceSlot slot,
        string filePath
    ) => LoadSourceAsync(slot, filePath);

    public async Task ExportAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_isExporting)
        {
            return;
        }

        if (_firstSource is null || _secondSource is null)
        {
            StatusMessage = AppStrings.MixChooseTwoSources;
            return;
        }

        if (string.IsNullOrWhiteSpace(ExportTitle))
        {
            StatusMessage = AppStrings.CatalogRenameRequired;
            return;
        }

        if (!CanRenderSources)
        {
            StatusMessage = AppStrings.WorkspaceMixSourceBusy;
            return;
        }

        SetExporting(true);
        StatusMessage = string.Empty;
        try
        {
            var first = _firstSource;
            var second = _secondSource;
            var firstSnapshot = await RenderSourceAsync(
                MixSourceSlot.First,
                first,
                _lifetimeCancellation.Token
            );
            var secondSnapshot = await RenderSourceAsync(
                MixSourceSlot.Second,
                second,
                _lifetimeCancellation.Token
            );
            if (firstSnapshot is null || secondSnapshot is null)
            {
                StatusMessage = AppStrings.MixChooseTwoSources;
                return;
            }

            var mixed = await Task.Run(
                () =>
                    TwoSourceAudioMixer.Mix(
                        firstSnapshot,
                        FirstVolumePercent / 100d,
                        secondSnapshot,
                        SecondVolumePercent / 100d,
                        _lifetimeCancellation.Token
                    ),
                _lifetimeCancellation.Token
            );
            var albumArt = !first.Metadata.AlbumArt.IsEmpty
                ? first.Metadata.AlbumArt
                : second.Metadata.AlbumArt;
            var result = await _exporter.ExportAsync(
                mixed,
                new ClipExportMetadata
                {
                    Title = ExportTitle.Trim(),
                    Artist = ExportArtist.Trim(),
                    Album = string.Empty,
                    AlbumArt = albumArt,
                },
                OutputFolder,
                _lifetimeCancellation.Token
            );
            StatusMessage = Format(
                AppStrings.ExportSucceeded,
                result.OutputPath
            );
        }
        catch (OperationCanceledException)
            when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusMessage = Format(
                AppStrings.ExportFailed,
                exception.Message
            );
        }
        finally
        {
            SetExporting(false);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
    }

    internal void SetSource(
        MixSourceSlot slot,
        ImportedAudioFile source
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        if (slot == MixSourceSlot.First)
        {
            _firstSource = source;
            _firstRenderer = null;
            _firstCanRender = null;
            OnPropertyChanged(nameof(HasFirstSource));
            OnPropertyChanged(nameof(FirstSourceTitle));
            OnPropertyChanged(nameof(FirstSourceDetail));
        }
        else
        {
            _secondSource = source;
            _secondRenderer = null;
            _secondCanRender = null;
            OnPropertyChanged(nameof(HasSecondSource));
            OnPropertyChanged(nameof(SecondSourceTitle));
            OnPropertyChanged(nameof(SecondSourceDetail));
        }

        RefreshDefaultMetadata();
        StatusMessage = string.Empty;
        OnPropertyChanged(nameof(CanExport));
        SourceChanged?.Invoke(
            this,
            new MixSourceChangedEventArgs(slot, source)
        );
    }

    internal void SetSourceRenderer(
        MixSourceSlot slot,
        Func<CancellationToken, Task<AudioBufferSnapshot?>> renderer,
        Func<bool> canRender
    )
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(canRender);
        if (slot == MixSourceSlot.First)
        {
            _firstRenderer = renderer;
            _firstCanRender = canRender;
        }
        else
        {
            _secondRenderer = renderer;
            _secondCanRender = canRender;
        }

        OnPropertyChanged(nameof(CanExport));
    }

    internal void NotifySourceRendererStateChanged() =>
        OnPropertyChanged(nameof(CanExport));

    private async Task LoadSourceAsync(
        MixSourceSlot slot,
        string filePath
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsBusy)
        {
            return;
        }

        SetLoadingSource(true);
        StatusMessage = AppStrings.MixLoadingSource;
        try
        {
            var fullPath = Path.GetFullPath(filePath);
            if (!_sourceCache.TryGetValue(fullPath, out var source))
            {
                source = await _importer.ImportAsync(
                    fullPath,
                    _lifetimeCancellation.Token
                );
                _sourceCache.Add(fullPath, source);
            }

            SetSource(slot, source);
        }
        catch (OperationCanceledException)
            when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusMessage = Format(
                AppStrings.MixSourceLoadFailed,
                exception.Message
            );
        }
        finally
        {
            SetLoadingSource(false);
        }
    }

    private void RefreshDefaultMetadata()
    {
        if (_firstSource is null || _secondSource is null)
        {
            return;
        }

        ExportTitle = string.Join(
            " / ",
            new[]
            {
                NormalizeTitle(_firstSource),
                NormalizeTitle(_secondSource),
            }
        );
        ExportArtist = string.Join(
            " / ",
            new[]
            {
                _firstSource.Metadata.Artist.Trim(),
                _secondSource.Metadata.Artist.Trim(),
            }
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
        );
    }

    private Task<AudioBufferSnapshot?> RenderSourceAsync(
        MixSourceSlot slot,
        ImportedAudioFile source,
        CancellationToken cancellationToken
    )
    {
        var renderer = slot == MixSourceSlot.First
            ? _firstRenderer
            : _secondRenderer;
        return renderer is null
            ? Task.FromResult<AudioBufferSnapshot?>(source.Snapshot)
            : renderer(cancellationToken);
    }

    private bool CanRenderSources =>
        (_firstCanRender?.Invoke() ?? true)
        && (_secondCanRender?.Invoke() ?? true);

    private void SetLoadingSource(bool value)
    {
        if (_isLoadingSource == value)
        {
            return;
        }

        _isLoadingSource = value;
        NotifyBusyStateChanged();
    }

    private void SetExporting(bool value)
    {
        if (_isExporting == value)
        {
            return;
        }

        _isExporting = value;
        OnPropertyChanged(nameof(ExportButtonText));
        NotifyBusyStateChanged();
    }

    private void NotifyBusyStateChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanChooseSources));
        OnPropertyChanged(nameof(CanExport));
    }

    private static string GetTitle(ImportedAudioFile? source) =>
        source is null
            ? AppStrings.MixNoSource
            : NormalizeTitle(source);

    private static string GetDetail(ImportedAudioFile? source)
    {
        if (source is null)
        {
            return string.Empty;
        }

        var artist = string.IsNullOrWhiteSpace(
            source.Metadata.Artist
        )
            ? AppStrings.ArtistFallback
            : source.Metadata.Artist.Trim();
        return string.Format(
            CultureInfo.CurrentCulture,
            "{0} · {1:0.0}s",
            artist,
            source.Snapshot.Duration.TotalSeconds
        );
    }

    private static string NormalizeTitle(ImportedAudioFile source) =>
        string.IsNullOrWhiteSpace(source.Metadata.Title)
            ? Path.GetFileNameWithoutExtension(source.SourcePath)
            : source.Metadata.Title.Trim();

    private static double NormalizeVolume(double value) =>
        Math.Clamp(
            Math.Round(value),
            StemRemixer.MinimumGain * 100,
            StemRemixer.MaximumGain * 100
        );

    private static string FormatVolume(double value) =>
        string.Create(
            CultureInfo.CurrentCulture,
            $"{value:0}%"
        );

    private static string Format(string format, string value) =>
        string.Format(
            CultureInfo.CurrentCulture,
            format,
            value
        );

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null
    ) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName)
        );
}
