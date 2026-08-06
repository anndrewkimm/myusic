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
    private readonly IAudioPreviewPlayer _previewPlayer;
    private readonly CancellationTokenSource _lifetimeCancellation =
        new();
    private readonly Dictionary<string, ImportedAudioFile> _sourceCache =
        new(StringComparer.OrdinalIgnoreCase);
    private ImportedAudioFile? _firstSource;
    private ImportedAudioFile? _secondSource;
    private MixSourceEditorIntegration? _firstEditor;
    private MixSourceEditorIntegration? _secondEditor;
    private MixRecipe _selectedRecipe = MixRecipe.Custom;
    private double _firstVolumePercent = 100;
    private double _secondVolumePercent = 100;
    private string _exportTitle = string.Empty;
    private string _exportArtist = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isLoadingSource;
    private bool _isPreparingMashup;
    private bool _isRenderingPreview;
    private bool _isExporting;
    private bool _disposed;

    public MixWindowViewModel(
        ClipCatalogService catalog,
        LocalAudioFileImporter importer,
        IClipExporter exporter,
        OutputFolderSettings outputSettings,
        IAudioPreviewPlayer previewPlayer
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
        _previewPlayer =
            previewPlayer
            ?? throw new ArgumentNullException(
                nameof(previewPlayer)
            );
        _previewPlayer.PlaybackStopped += OnPreviewStopped;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<MixSourceChangedEventArgs>?
        SourceChanged;

    public event EventHandler? SourcesSwapped;

    public ObservableCollection<MixCatalogSource> CatalogSources
    {
        get;
    } = [];

    public MixRecipe SelectedRecipe => _selectedRecipe;

    public bool IsMashupRecipe =>
        SelectedRecipe == MixRecipe.VocalsAndInstrumentalMashup;

    public bool IsSequentialRecipe =>
        SelectedRecipe == MixRecipe.Sequential;

    public bool IsCustomRecipe =>
        SelectedRecipe == MixRecipe.Custom;

    public bool HasFirstSource => _firstSource is not null;

    public bool HasSecondSource => _secondSource is not null;

    public string FirstSourceTitle => GetTitle(_firstSource);

    public string SecondSourceTitle => GetTitle(_secondSource);

    public string FirstSourceDetail => GetDetail(_firstSource);

    public string SecondSourceDetail => GetDetail(_secondSource);

    public string FirstSourceLabel => IsMashupRecipe
        ? AppStrings.MixVocalsSource
        : AppStrings.MixFirstSource;

    public string SecondSourceLabel => IsMashupRecipe
        ? AppStrings.MixInstrumentalSource
        : AppStrings.MixSecondSource;

    public string FirstEditButtonText => IsMashupRecipe
        ? AppStrings.MixEditVocalsSource
        : AppStrings.WorkspaceMixSourceA;

    public string SecondEditButtonText => IsMashupRecipe
        ? AppStrings.MixEditInstrumentalSource
        : AppStrings.WorkspaceMixSourceB;

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
            StopPreview();
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
            StopPreview();
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

    public bool IsBusy =>
        _isLoadingSource
        || _isPreparingMashup
        || _isRenderingPreview
        || _isExporting;

    public bool IsPreparingMashup => _isPreparingMashup;

    public double MashupProgressPercent =>
        ((_firstEditor?.GetStemProgressPercent() ?? 0d)
            + (_secondEditor?.GetStemProgressPercent() ?? 0d))
        / 2d;

    public string MashupProgressText => string.Create(
        CultureInfo.CurrentCulture,
        $"{MashupProgressPercent:0}%"
    );

    public bool CanChooseSources => !IsBusy;

    public bool CanSelectRecipe => !IsBusy;

    public bool CanEditFirstSource => HasFirstSource && !IsBusy;

    public bool CanEditSecondSource => HasSecondSource && !IsBusy;

    public bool CanAdjustFirstVolume => HasFirstSource && !IsBusy;

    public bool CanAdjustSecondVolume => HasSecondSource && !IsBusy;

    public bool CanEditMetadata => !IsBusy;

    public bool CanSwapSources =>
        IsMashupRecipe
        && HasFirstSource
        && HasSecondSource
        && !IsBusy;

    public bool IsPlaying => _previewPlayer.IsPlaying;

    public bool CanPreview => IsPlaying || CanRenderMix;

    public bool CanExport =>
        CanRenderMix
        && !string.IsNullOrWhiteSpace(ExportTitle);

    public string PreviewButtonText => IsPlaying
        ? AppStrings.MixStopPreview
        : AppStrings.MixPreview;

    public string ExportButtonText => _isExporting
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

    public async Task SelectRecipeAsync(MixRecipe recipe)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsBusy)
        {
            return;
        }

        StopPreview();
        _selectedRecipe = recipe;
        NotifyRecipeChanged();
        StatusMessage = string.Empty;

        if (recipe == MixRecipe.VocalsAndInstrumentalMashup)
        {
            await PrepareMashupAsync();
        }
        else if (recipe == MixRecipe.Sequential)
        {
            ApplyStemRole(_firstEditor, MixStemRole.FullMix);
            ApplyStemRole(_secondEditor, MixStemRole.FullMix);
            if (HasFirstSource && HasSecondSource)
            {
                StatusMessage = AppStrings.MixSequentialReady;
            }
        }

        NotifyActionStateChanged();
    }

    public void SwapSources()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!CanSwapSources)
        {
            return;
        }

        StopPreview();
        (_firstSource, _secondSource) =
            (_secondSource, _firstSource);
        (_firstEditor, _secondEditor) =
            (_secondEditor, _firstEditor);
        (_firstVolumePercent, _secondVolumePercent) =
            (_secondVolumePercent, _firstVolumePercent);
        ApplyMashupRoles();
        RefreshDefaultMetadata();
        NotifyAllSourceProperties();
        StatusMessage = AppStrings.MixSourcesSwapped;
        SourcesSwapped?.Invoke(this, EventArgs.Empty);
    }

    public async Task PreviewAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsPlaying)
        {
            StopPreview();
            return;
        }

        if (!CanRenderMix)
        {
            StatusMessage = GetUnavailableMessage();
            return;
        }

        SetRenderingPreview(true);
        StatusMessage = AppStrings.MixRenderingPreview;
        try
        {
            var mixed = await RenderMixAsync(
                _lifetimeCancellation.Token
            );
            if (mixed is null)
            {
                StatusMessage = AppStrings.MixChooseTwoSources;
                return;
            }

            _previewPlayer.Play(mixed);
            StatusMessage = AppStrings.MixPreviewReady;
            NotifyPreviewStateChanged();
        }
        catch (OperationCanceledException)
            when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusMessage = Format(
                AppStrings.PreviewFailed,
                exception.Message
            );
        }
        finally
        {
            SetRenderingPreview(false);
        }
    }

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

        if (!CanRenderSources || !IsRecipeReady)
        {
            StatusMessage = GetUnavailableMessage();
            return;
        }

        StopPreview();
        SetExporting(true);
        StatusMessage = string.Empty;
        try
        {
            var mixed = await RenderMixAsync(
                _lifetimeCancellation.Token
            );
            if (mixed is null)
            {
                StatusMessage = AppStrings.MixChooseTwoSources;
                return;
            }

            var first = _firstSource;
            var second = _secondSource;
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
        _previewPlayer.PlaybackStopped -= OnPreviewStopped;
        _previewPlayer.Dispose();
        _lifetimeCancellation.Dispose();
    }

    internal async Task SetSourceAsync(
        MixSourceSlot slot,
        ImportedAudioFile source
    )
    {
        SetSourceCore(slot, source);
        await ApplyRecipeAfterSourceChangedAsync(slot);
    }

    internal void SetSourceEditor(
        MixSourceSlot slot,
        MixSourceEditorIntegration integration
    )
    {
        ArgumentNullException.ThrowIfNull(integration);
        if (slot == MixSourceSlot.First)
        {
            _firstEditor = integration;
        }
        else
        {
            _secondEditor = integration;
        }

        NotifySourceRendererStateChanged();
    }

    internal void SetSourceRenderer(
        MixSourceSlot slot,
        Func<CancellationToken, Task<AudioBufferSnapshot?>> renderer,
        Func<bool> canRender
    )
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(canRender);
        SetSourceEditor(
            slot,
            new MixSourceEditorIntegration(
                renderer,
                canRender,
                _ => Task.FromResult(false),
                () => false,
                () => 0,
                _ => { }
            )
        );
    }

    internal void NotifySourceRendererStateChanged()
    {
        OnPropertyChanged(nameof(MashupProgressPercent));
        OnPropertyChanged(nameof(MashupProgressText));
        NotifyActionStateChanged();
    }

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

        ImportedAudioFile? loadedSource = null;
        SetLoadingSource(true);
        StatusMessage = AppStrings.MixLoadingSource;
        try
        {
            var fullPath = Path.GetFullPath(filePath);
            if (!_sourceCache.TryGetValue(fullPath, out loadedSource))
            {
                loadedSource = await _importer.ImportAsync(
                    fullPath,
                    _lifetimeCancellation.Token
                );
                _sourceCache.Add(fullPath, loadedSource);
            }

            SetSourceCore(slot, loadedSource);
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

        if (loadedSource is not null)
        {
            await ApplyRecipeAfterSourceChangedAsync(slot);
        }
    }

    private void SetSourceCore(
        MixSourceSlot slot,
        ImportedAudioFile source
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        StopPreview();
        if (slot == MixSourceSlot.First)
        {
            _firstSource = source;
            _firstEditor = null;
        }
        else
        {
            _secondSource = source;
            _secondEditor = null;
        }

        RefreshDefaultMetadata();
        StatusMessage = string.Empty;
        NotifyAllSourceProperties();
        SourceChanged?.Invoke(
            this,
            new MixSourceChangedEventArgs(slot, source)
        );
    }

    private async Task ApplyRecipeAfterSourceChangedAsync(
        MixSourceSlot slot
    )
    {
        if (SelectedRecipe == MixRecipe.VocalsAndInstrumentalMashup)
        {
            await PrepareMashupAsync();
        }
        else if (SelectedRecipe == MixRecipe.Sequential)
        {
            ApplyStemRole(
                GetEditor(slot),
                MixStemRole.FullMix
            );
        }

        NotifyActionStateChanged();
    }

    private async Task PrepareMashupAsync()
    {
        if (
            _firstSource is null
            || _secondSource is null
            || _firstEditor is null
            || _secondEditor is null
        )
        {
            StatusMessage = AppStrings.MixMashupChooseSources;
            return;
        }

        SetPreparingMashup(true);
        StatusMessage = AppStrings.MixPreparingMashup;
        try
        {
            var firstTask = _firstEditor.EnsureStemsAsync(
                _lifetimeCancellation.Token
            );
            var secondTask = _secondEditor.EnsureStemsAsync(
                _lifetimeCancellation.Token
            );
            var results = await Task.WhenAll(firstTask, secondTask);
            if (
                results.All(result => result)
                && _firstEditor.HasSeparatedStems()
                && _secondEditor.HasSeparatedStems()
            )
            {
                ApplyMashupRoles();
                StatusMessage = AppStrings.MixMashupReady;
            }
            else
            {
                StatusMessage = AppStrings.MixMashupIncomplete;
            }
        }
        catch (OperationCanceledException)
            when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusMessage = Format(
                AppStrings.MixMashupFailed,
                exception.Message
            );
        }
        finally
        {
            SetPreparingMashup(false);
        }
    }

    private void ApplyMashupRoles()
    {
        ApplyStemRole(_firstEditor, MixStemRole.VocalsOnly);
        ApplyStemRole(
            _secondEditor,
            MixStemRole.InstrumentalOnly
        );
    }

    private static void ApplyStemRole(
        MixSourceEditorIntegration? editor,
        MixStemRole role
    )
    {
        if (editor?.HasSeparatedStems() == true)
        {
            editor.ApplyStemRole(role);
        }
    }

    private async Task<AudioBufferSnapshot?> RenderMixAsync(
        CancellationToken cancellationToken
    )
    {
        var first = _firstSource;
        var second = _secondSource;
        if (first is null || second is null)
        {
            return null;
        }

        var firstSnapshot = await RenderSourceAsync(
            _firstEditor,
            first,
            cancellationToken
        );
        var secondSnapshot = await RenderSourceAsync(
            _secondEditor,
            second,
            cancellationToken
        );
        if (firstSnapshot is null || secondSnapshot is null)
        {
            return null;
        }

        var recipe = SelectedRecipe;
        var firstGain = FirstVolumePercent / 100d;
        var secondGain = SecondVolumePercent / 100d;
        return await Task.Run(
            () =>
                recipe == MixRecipe.Sequential
                    ? TwoSourceAudioMixer.ArrangeSequentially(
                        firstSnapshot,
                        firstGain,
                        secondSnapshot,
                        secondGain,
                        cancellationToken
                    )
                    : TwoSourceAudioMixer.Mix(
                        firstSnapshot,
                        firstGain,
                        secondSnapshot,
                        secondGain,
                        cancellationToken
                    ),
            cancellationToken
        );
    }

    private static Task<AudioBufferSnapshot?> RenderSourceAsync(
        MixSourceEditorIntegration? editor,
        ImportedAudioFile source,
        CancellationToken cancellationToken
    ) =>
        editor is null
            ? Task.FromResult<AudioBufferSnapshot?>(source.Snapshot)
            : editor.RenderAsync(cancellationToken);

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

    private bool CanRenderMix =>
        HasFirstSource
        && HasSecondSource
        && CanRenderSources
        && IsRecipeReady
        && !IsBusy;

    private bool CanRenderSources =>
        (_firstEditor?.CanRender() ?? true)
        && (_secondEditor?.CanRender() ?? true);

    private bool IsRecipeReady =>
        !IsMashupRecipe
        || (
            _firstEditor?.HasSeparatedStems() == true
            && _secondEditor?.HasSeparatedStems() == true
        );

    private string GetUnavailableMessage() =>
        !HasFirstSource || !HasSecondSource
            ? AppStrings.MixChooseTwoSources
            : IsMashupRecipe && !IsRecipeReady
                ? AppStrings.MixMashupIncomplete
                : AppStrings.WorkspaceMixSourceBusy;

    private MixSourceEditorIntegration? GetEditor(
        MixSourceSlot slot
    ) =>
        slot == MixSourceSlot.First
            ? _firstEditor
            : _secondEditor;

    private void SetLoadingSource(bool value)
    {
        if (_isLoadingSource == value)
        {
            return;
        }

        _isLoadingSource = value;
        NotifyBusyStateChanged();
    }

    private void SetPreparingMashup(bool value)
    {
        if (_isPreparingMashup == value)
        {
            return;
        }

        _isPreparingMashup = value;
        OnPropertyChanged(nameof(IsPreparingMashup));
        NotifyBusyStateChanged();
    }

    private void SetRenderingPreview(bool value)
    {
        if (_isRenderingPreview == value)
        {
            return;
        }

        _isRenderingPreview = value;
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
        OnPropertyChanged(nameof(CanSelectRecipe));
        OnPropertyChanged(nameof(CanEditFirstSource));
        OnPropertyChanged(nameof(CanEditSecondSource));
        OnPropertyChanged(nameof(CanAdjustFirstVolume));
        OnPropertyChanged(nameof(CanAdjustSecondVolume));
        OnPropertyChanged(nameof(CanEditMetadata));
        OnPropertyChanged(nameof(CanSwapSources));
        NotifyActionStateChanged();
    }

    private void NotifyRecipeChanged()
    {
        OnPropertyChanged(nameof(SelectedRecipe));
        OnPropertyChanged(nameof(IsMashupRecipe));
        OnPropertyChanged(nameof(IsSequentialRecipe));
        OnPropertyChanged(nameof(IsCustomRecipe));
        OnPropertyChanged(nameof(FirstSourceLabel));
        OnPropertyChanged(nameof(SecondSourceLabel));
        OnPropertyChanged(nameof(FirstEditButtonText));
        OnPropertyChanged(nameof(SecondEditButtonText));
        OnPropertyChanged(nameof(CanSwapSources));
    }

    private void NotifyAllSourceProperties()
    {
        OnPropertyChanged(nameof(HasFirstSource));
        OnPropertyChanged(nameof(HasSecondSource));
        OnPropertyChanged(nameof(FirstSourceTitle));
        OnPropertyChanged(nameof(SecondSourceTitle));
        OnPropertyChanged(nameof(FirstSourceDetail));
        OnPropertyChanged(nameof(SecondSourceDetail));
        OnPropertyChanged(nameof(FirstVolumePercent));
        OnPropertyChanged(nameof(SecondVolumePercent));
        OnPropertyChanged(nameof(FirstVolumeText));
        OnPropertyChanged(nameof(SecondVolumeText));
        OnPropertyChanged(nameof(CanEditFirstSource));
        OnPropertyChanged(nameof(CanEditSecondSource));
        OnPropertyChanged(nameof(CanAdjustFirstVolume));
        OnPropertyChanged(nameof(CanAdjustSecondVolume));
        OnPropertyChanged(nameof(CanSwapSources));
        NotifyActionStateChanged();
    }

    private void NotifyActionStateChanged()
    {
        OnPropertyChanged(nameof(CanPreview));
        OnPropertyChanged(nameof(CanExport));
    }

    private void StopPreview()
    {
        if (_previewPlayer.IsPlaying)
        {
            _previewPlayer.Stop();
        }

        NotifyPreviewStateChanged();
    }

    private void OnPreviewStopped(object? sender, EventArgs args) =>
        NotifyPreviewStateChanged();

    private void NotifyPreviewStateChanged()
    {
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PreviewButtonText));
        OnPropertyChanged(nameof(CanPreview));
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
