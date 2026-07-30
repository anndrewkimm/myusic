using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using Hookline.Audio;

namespace Hookline.App;

public sealed class TrimViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly TimeSpan MinimumSelection =
        TimeSpan.FromMilliseconds(1);

    private readonly IClipExporter _exporter;
    private readonly IAudioPreviewPlayer _previewPlayer;
    private readonly OutputFolderSettings _outputSettings;
    private readonly IStemIsolationService _stemIsolationService;
    private readonly CancellationTokenSource _lifetimeCancellation =
        new();
    private CancellationTokenSource? _stemCancellation;

    private TimeSpan? _selectionStart;
    private TimeSpan? _selectionEnd;
    private TimeSpan? _playhead;
    private SelectionEdge _activeEdge = SelectionEdge.End;
    private string _statusMessage = string.Empty;
    private EditEffectSelection _editEffectSelection =
        EditEffectSelection.None;
    private GraphicEqualizerSelection _equalizerSelection =
        GraphicEqualizerSelection.Flat;
    private readonly IReadOnlyList<EqualizerBandViewModel>
        _equalizerBands;
    private int _loopCount = 1;
    private bool _isEqualizerExpanded;
    private bool _isExporting;
    private bool _isStemSeparating;
    private bool _isSixStemExperimental;
    private bool _isStemBandView;
    private double _stemProgressPercent;
    private SeparatedStemSet? _separatedStemSet;
    private IReadOnlyList<StemVolumeViewModel> _stemVolumes =
        Array.Empty<StemVolumeViewModel>();
    private bool _disposed;

    public TrimViewModel(
        TrimSession session,
        IClipExporter exporter,
        IAudioPreviewPlayer previewPlayer,
        OutputFolderSettings outputSettings,
        IStemIsolationService stemIsolationService
    )
    {
        Session =
            session
            ?? throw new ArgumentNullException(nameof(session));
        _exporter =
            exporter
            ?? throw new ArgumentNullException(nameof(exporter));
        _previewPlayer =
            previewPlayer
            ?? throw new ArgumentNullException(nameof(previewPlayer));
        _outputSettings =
            outputSettings
            ?? throw new ArgumentNullException(nameof(outputSettings));
        _stemIsolationService =
            stemIsolationService
            ?? throw new ArgumentNullException(
                nameof(stemIsolationService)
            );
        _equalizerBands = GraphicEqualizerBands
            .CenterFrequencies
            .Select(
                (frequency, index) =>
                    new EqualizerBandViewModel(
                        index,
                        frequency,
                        OnEqualizerBandChanged
                    )
            )
            .ToArray();

        _previewPlayer.PositionChanged += OnPreviewPositionChanged;
        _previewPlayer.PlaybackStopped += OnPreviewStopped;
        if (
            session.InitialSelectionStart is { } initialStart
            && session.InitialSelectionEnd is { } initialEnd
        )
        {
            SetSelection(initialStart, initialEnd);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public TrimSession Session { get; }

    public AudioBufferSnapshot Snapshot => Session.Snapshot;

    public string TrackTitle =>
        string.IsNullOrWhiteSpace(Session.Track.Title)
            ? AppStrings.TrackFallback
            : Session.Track.Title;

    public string TrackArtist =>
        string.IsNullOrWhiteSpace(Session.Track.Artist)
            ? AppStrings.ArtistFallback
            : Session.Track.Artist;

    public TimeSpan DisplayStart =>
        Snapshot.AvailableStart ?? Snapshot.RequestedStart;

    public TimeSpan DisplayEnd =>
        Snapshot.AvailableEnd ?? Snapshot.RequestedEnd;

    public TimeSpan? SelectionStart
    {
        get => _selectionStart;
        private set
        {
            if (_selectionStart == value)
            {
                return;
            }

            _selectionStart = value;
            OnPropertyChanged();
        }
    }

    public TimeSpan? SelectionEnd
    {
        get => _selectionEnd;
        private set
        {
            if (_selectionEnd == value)
            {
                return;
            }

            _selectionEnd = value;
            OnPropertyChanged();
        }
    }

    public TimeSpan? Playhead
    {
        get => _playhead;
        private set
        {
            if (_playhead == value)
            {
                return;
            }

            _playhead = value;
            OnPropertyChanged();
        }
    }

    public string SelectionStartText =>
        FormatTime(SelectionStart);

    public string SelectionEndText => FormatTime(SelectionEnd);

    public string SelectionDurationText =>
        SelectionStart is { } start
        && SelectionEnd is { } end
            ? FormatDuration(end - start)
            : AppStrings.EmptyTime;

    public bool HasSelection =>
        SelectionStart.HasValue && SelectionEnd.HasValue;

    public bool SelectionOverlapsExcluded =>
        SelectionStart is { } start
        && SelectionEnd is { } end
        && Snapshot.ExcludedRanges.Any(
            range => range.Overlaps(start, end)
        );

    public bool CanPreview =>
        HasSelection && !_isExporting && !_isStemSeparating;

    public bool CanExport =>
        HasSelection && !_isExporting && !_isStemSeparating;

    public bool AreEffectsEnabled =>
        !_isExporting && !_isStemSeparating;

    public bool CanEditSelection =>
        !_isExporting && !_isStemSeparating;

    public bool CanIsolateStems =>
        HasSelection && !_isExporting && !_isStemSeparating;

    public bool CanCancelStemIsolation => _isStemSeparating;

    public bool HasSeparatedStems => _separatedStemSet is not null;

    public IReadOnlyList<StemVolumeViewModel> StemVolumes =>
        _stemVolumes;

    public bool IsStemBandView => _isStemBandView;

    public bool IsStemSliderView => !_isStemBandView;

    public void SetStemBandView(bool isBandView)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_isStemBandView == isBandView)
        {
            return;
        }

        _isStemBandView = isBandView;
        OnPropertyChanged(nameof(IsStemBandView));
        OnPropertyChanged(nameof(IsStemSliderView));
    }

    public bool IsSixStemExperimental
    {
        get => _isSixStemExperimental;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_isStemSeparating || _isSixStemExperimental == value)
            {
                return;
            }

            StopPreview();
            _isSixStemExperimental = value;
            ResetStemMix();
            StatusMessage = string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedStemMode));
            OnPropertyChanged(nameof(SelectedStemModel));
        }
    }

    public StemSeparationMode SelectedStemMode =>
        IsSixStemExperimental
            ? StemSeparationMode.SixStemExperimental
            : StemSeparationMode.FourStem;

    public StemModelDescriptor SelectedStemModel =>
        _stemIsolationService.GetModel(SelectedStemMode);

    public bool IsStemSeparating
    {
        get => _isStemSeparating;
        private set
        {
            if (_isStemSeparating == value)
            {
                return;
            }

            _isStemSeparating = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanPreview));
            OnPropertyChanged(nameof(CanExport));
            OnPropertyChanged(nameof(AreEffectsEnabled));
            OnPropertyChanged(nameof(CanEditSelection));
            OnPropertyChanged(nameof(CanIsolateStems));
            OnPropertyChanged(nameof(CanCancelStemIsolation));
        }
    }

    public double StemProgressPercent
    {
        get => _stemProgressPercent;
        private set
        {
            var normalized = Math.Clamp(value, 0, 100);
            if (Math.Abs(_stemProgressPercent - normalized) < 0.01)
            {
                return;
            }

            _stemProgressPercent = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StemProgressText));
        }
    }

    public string StemProgressText =>
        string.Create(
            CultureInfo.CurrentCulture,
            $"{StemProgressPercent:0}%"
        );

    public bool IsPlaying => _previewPlayer.IsPlaying;

    public string PreviewButtonText =>
        IsPlaying ? AppStrings.StopPreview : AppStrings.Preview;

    public string ExportButtonText =>
        IsExporting ? AppStrings.Exporting : AppStrings.Export;

    public double SpeedMultiplier
    {
        get => _editEffectSelection.SpeedMultiplier;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var normalized = Math.Clamp(
                Math.Round(value, 2),
                ClipEffectSettings.MinimumSpeedMultiplier,
                ClipEffectSettings.MaximumSpeedMultiplier
            );
            var selection =
                _editEffectSelection.AdjustSpeed(normalized);
            if (selection.Equals(_editEffectSelection))
            {
                return;
            }

            _editEffectSelection = selection;
            EffectsChanged(
                nameof(SpeedMultiplier),
                nameof(SpeedMultiplierText),
                nameof(EditEffectPreset),
                nameof(EditEffectPresetText)
            );
        }
    }

    public double ReverbAmountPercent
    {
        get => Math.Round(
            _editEffectSelection.ReverbWetMix * 100
        );
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var normalized = Math.Clamp(
                Math.Round(value),
                ClipEffectSettings.MinimumReverbWetMix * 100,
                ClipEffectSettings.MaximumReverbWetMix * 100
            );
            var selection = _editEffectSelection.AdjustReverb(
                normalized / 100
            );
            if (selection.Equals(_editEffectSelection))
            {
                return;
            }

            _editEffectSelection = selection;
            EffectsChanged(
                nameof(ReverbAmountPercent),
                nameof(ReverbAmountText),
                nameof(EditEffectPreset),
                nameof(EditEffectPresetText)
            );
        }
    }

    public double RotationRateHertz
    {
        get => _editEffectSelection.RotationRateHertz;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var rounded = Math.Round(value, 2);
            var normalized =
                rounded <= 0
                    ? 0
                    : Math.Clamp(
                        rounded,
                        ClipEffectSettings
                            .MinimumRotationRateHertz,
                        ClipEffectSettings
                            .MaximumRotationRateHertz
                    );
            var selection =
                _editEffectSelection.AdjustRotation(normalized);
            if (selection.Equals(_editEffectSelection))
            {
                return;
            }

            _editEffectSelection = selection;
            EffectsChanged(
                nameof(RotationRateHertz),
                nameof(RotationRateText),
                nameof(EditEffectPreset),
                nameof(EditEffectPresetText)
            );
        }
    }

    public EditEffectPreset EditEffectPreset =>
        _editEffectSelection.Preset;

    public string EditEffectPresetText =>
        _editEffectSelection.Preset switch
        {
            EditEffectPreset.None => AppStrings.EditPresetNone,
            EditEffectPreset.SlowedReverb =>
                AppStrings.SlowedReverb,
            EditEffectPreset.SpedUp => AppStrings.SpedUp,
            EditEffectPreset.EightDAudio =>
                AppStrings.EightDAudio,
            EditEffectPreset.Custom =>
                AppStrings.EditPresetCustom,
            _ => throw new InvalidOperationException(),
        };

    public IReadOnlyList<EqualizerBandViewModel> EqualizerBands =>
        _equalizerBands;

    public EqualizerPreset EqualizerPreset =>
        _equalizerSelection.Preset;

    public string EqualizerPresetText =>
        _equalizerSelection.Preset switch
        {
            EqualizerPreset.Flat => AppStrings.EqualizerFlat,
            EqualizerPreset.BassBoost => AppStrings.BassBoost,
            EqualizerPreset.TrebleBoost =>
                AppStrings.EqualizerTrebleBoost,
            EqualizerPreset.Vocal => AppStrings.EqualizerVocal,
            EqualizerPreset.Bright => AppStrings.EqualizerBright,
            EqualizerPreset.Mellow => AppStrings.EqualizerMellow,
            EqualizerPreset.Custom => AppStrings.EqualizerCustom,
            _ => throw new InvalidOperationException(),
        };

    public bool IsEqualizerExpanded
    {
        get => _isEqualizerExpanded;
        private set
        {
            if (_isEqualizerExpanded == value)
            {
                return;
            }

            _isEqualizerExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(
                nameof(EqualizerExpandButtonText)
            );
        }
    }

    public string EqualizerExpandButtonText =>
        IsEqualizerExpanded
            ? AppStrings.EqualizerHide
            : AppStrings.EqualizerTune;

    public int LoopCount
    {
        get => _loopCount;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var normalized = Math.Clamp(
                value,
                ClipEffectSettings.MinimumLoopCount,
                ClipEffectSettings.MaximumLoopCount
            );
            if (_loopCount == normalized)
            {
                return;
            }

            _loopCount = normalized;
            EffectsChanged(
                nameof(LoopCount),
                nameof(LoopCountText)
            );
        }
    }

    public string SpeedMultiplierText =>
        string.Create(
            CultureInfo.CurrentCulture,
            $"{SpeedMultiplier:0.00}×"
        );

    public string ReverbAmountText =>
        ReverbAmountPercent == 0
            ? AppStrings.EffectOff
            : string.Create(
                CultureInfo.CurrentCulture,
                $"{ReverbAmountPercent:0}%"
            );

    public string RotationRateText =>
        RotationRateHertz == 0
            ? AppStrings.EffectOff
            : string.Create(
                CultureInfo.CurrentCulture,
                $"{1 / RotationRateHertz:0.0}s/cycle"
            );

    public string LoopCountText =>
        LoopCount == 1
            ? AppStrings.EffectOff
            : string.Create(
                CultureInfo.CurrentCulture,
                $"{LoopCount}×"
            );

    public void ApplyEditEffectPreset(EditEffectPreset preset)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var selection = EditEffectSelection.FromPreset(preset);
        if (selection.Equals(_editEffectSelection))
        {
            return;
        }

        _editEffectSelection = selection;
        EffectsChanged(
            nameof(SpeedMultiplier),
            nameof(SpeedMultiplierText),
            nameof(ReverbAmountPercent),
            nameof(ReverbAmountText),
            nameof(RotationRateHertz),
            nameof(RotationRateText),
            nameof(EditEffectPreset),
            nameof(EditEffectPresetText)
        );
    }

    public void ApplyEqualizerPreset(EqualizerPreset preset)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var selection = GraphicEqualizerSelection.FromPreset(
            preset
        );
        if (selection.Equals(_equalizerSelection))
        {
            return;
        }

        _equalizerSelection = selection;
        for (var index = 0; index < _equalizerBands.Count; index++)
        {
            _equalizerBands[index].SetFromPreset(
                selection.Curve[index]
            );
        }

        EffectsChanged(
            nameof(EqualizerPreset),
            nameof(EqualizerPresetText)
        );
    }

    public void ToggleEqualizerExpanded()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IsEqualizerExpanded = !IsEqualizerExpanded;
    }

    public async Task<bool?> CheckStemModelAvailableAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsStemSeparating)
        {
            return null;
        }

        StatusMessage = AppStrings.CheckingStemModel;
        try
        {
            var available =
                await _stemIsolationService.IsModelAvailableAsync(
                    SelectedStemMode,
                    _lifetimeCancellation.Token
                );
            StatusMessage = string.Empty;
            return available;
        }
        catch (OperationCanceledException)
            when (_lifetimeCancellation.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                AppStrings.StemIsolationFailed,
                exception.Message
            );
            return null;
        }
    }

    public async Task IsolateStemsAsync(bool downloadModel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsStemSeparating)
        {
            return;
        }

        if (
            SelectionStart is not { } start
            || SelectionEnd is not { } end
        )
        {
            StatusMessage = AppStrings.SelectFirst;
            return;
        }

        if (
            end - start
                > OnnxStemSeparator.MaximumSelectionDuration
        )
        {
            StatusMessage = AppStrings.StemSelectionTooLong;
            return;
        }

        var selection = CreateRawSelectionSnapshot();
        if (selection is null || selection.Audio.IsEmpty)
        {
            StatusMessage = AppStrings.SelectionHasNoAudio;
            return;
        }

        StopPreview();
        ResetStemMix();
        var mode = SelectedStemMode;
        var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                _lifetimeCancellation.Token
            );
        _stemCancellation = cancellation;
        IsStemSeparating = true;
        StemProgressPercent = 0;
        try
        {
            if (downloadModel)
            {
                StatusMessage = AppStrings.DownloadingStemModel;
                var downloadProgress = new Progress<double>(
                    value =>
                        StemProgressPercent = value * 100
                );
                await _stemIsolationService.DownloadModelAsync(
                    mode,
                    downloadProgress,
                    cancellation.Token
                );
            }

            cancellation.Token.ThrowIfCancellationRequested();
            StatusMessage = AppStrings.LoadingStemModel;
            StemProgressPercent = 0;
            var separationProgress = new Progress<double>(
                value => StemProgressPercent = value * 100
            );
            var result =
                await _stemIsolationService.SeparateAsync(
                    selection,
                    mode,
                    separationProgress,
                    cancellation.Token
                );
            cancellation.Token.ThrowIfCancellationRequested();
            if (
                SelectionStart != start
                || SelectionEnd != end
            )
            {
                return;
            }

            _separatedStemSet = result;
            _stemVolumes = OrderStemsForDisplay(result)
                .Select(
                    stem =>
                        new StemVolumeViewModel(
                            stem.Kind,
                            OnStemVolumeChanged
                        )
                )
                .ToArray();
            StemProgressPercent = 100;
            OnPropertyChanged(nameof(StemVolumes));
            OnPropertyChanged(nameof(HasSeparatedStems));
            StatusMessage = AppStrings.StemIsolationReady;
        }
        catch (OperationCanceledException)
            when (cancellation.IsCancellationRequested)
        {
            if (!_lifetimeCancellation.IsCancellationRequested)
            {
                StatusMessage =
                    AppStrings.StemIsolationCanceled;
            }
        }
        catch (Exception exception)
        {
            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                AppStrings.StemIsolationFailed,
                exception.Message
            );
        }
        finally
        {
            if (ReferenceEquals(_stemCancellation, cancellation))
            {
                _stemCancellation = null;
            }

            cancellation.Dispose();
            IsStemSeparating = false;
        }
    }

    public void CancelStemIsolation()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_stemCancellation is null)
        {
            return;
        }

        StatusMessage = AppStrings.CancelingStemIsolation;
        _stemCancellation.Cancel();
    }

    public bool IsExporting
    {
        get => _isExporting;
        private set
        {
            if (_isExporting == value)
            {
                return;
            }

            _isExporting = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanPreview));
            OnPropertyChanged(nameof(CanExport));
            OnPropertyChanged(nameof(AreEffectsEnabled));
            OnPropertyChanged(nameof(CanEditSelection));
            OnPropertyChanged(nameof(CanIsolateStems));
            OnPropertyChanged(nameof(ExportButtonText));
        }
    }

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

    public string OutputFolder => _outputSettings.OutputFolder;

    public bool ShowSpotifyLocalFilesHint =>
        _outputSettings.ShouldShowSpotifyLocalFilesHint;

    public void SetSelection(TimeSpan start, TimeSpan end)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (DisplayEnd <= DisplayStart)
        {
            ClearSelection();
            return;
        }

        var clampedStart = Clamp(start, DisplayStart, DisplayEnd);
        var clampedEnd = Clamp(end, DisplayStart, DisplayEnd);
        if (clampedEnd < clampedStart)
        {
            (clampedStart, clampedEnd) = (
                clampedEnd,
                clampedStart
            );
        }

        if (clampedEnd - clampedStart < MinimumSelection)
        {
            ClearSelection();
            return;
        }

        StopPreview();
        CancelAndResetStemMix();
        SelectionStart = clampedStart;
        SelectionEnd = clampedEnd;
        StatusMessage = string.Empty;
        NotifySelectionProperties();
    }

    public void SetActiveEdge(SelectionEdge edge) =>
        _activeEdge = edge;

    public void NudgeActiveEdge(TimeSpan change) =>
        Nudge(_activeEdge, change);

    public void Nudge(SelectionEdge edge, TimeSpan change)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (
            SelectionStart is not { } start
            || SelectionEnd is not { } end
        )
        {
            return;
        }

        StopPreview();
        CancelAndResetStemMix();
        if (edge == SelectionEdge.Start)
        {
            var maximum = end - MinimumSelection;
            SelectionStart = Clamp(
                start + change,
                DisplayStart,
                maximum
            );
        }
        else
        {
            var minimum = start + MinimumSelection;
            SelectionEnd = Clamp(
                end + change,
                minimum,
                DisplayEnd
            );
        }

        StatusMessage = string.Empty;
        NotifySelectionProperties();
    }

    public void TogglePreview()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_previewPlayer.IsPlaying)
        {
            StopPreview();
            return;
        }

        StartPreview();
    }

    private void StartPreview()
    {
        try
        {
            var selection = CreateSelectionSnapshot();
            if (selection is null)
            {
                return;
            }

            if (selection.Audio.IsEmpty)
            {
                StatusMessage = AppStrings.SelectionHasNoAudio;
                return;
            }

            _previewPlayer.Play(selection);
            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(PreviewButtonText));
        }
        catch (Exception exception)
        {
            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                AppStrings.PreviewFailed,
                exception.Message
            );
        }
    }

    public async Task ExportAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsExporting)
        {
            return;
        }

        if (!HasSelection)
        {
            StatusMessage = AppStrings.SelectFirst;
            return;
        }

        StopPreview();
        IsExporting = true;
        StatusMessage = string.Empty;
        try
        {
            var selection = CreateSelectionSnapshot();
            if (selection is null)
            {
                return;
            }

            if (selection.Audio.IsEmpty)
            {
                StatusMessage = AppStrings.SelectionHasNoAudio;
                return;
            }

            var result = await _exporter.ExportAsync(
                selection,
                new ClipExportMetadata
                {
                    Title = Session.Track.Title,
                    Artist = Session.Track.Artist,
                    Album = Session.Track.Album,
                    AlbumArt = Session.Track.AlbumArt,
                },
                OutputFolder,
                _lifetimeCancellation.Token
            );
            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
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
            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                AppStrings.ExportFailed,
                exception.Message
            );
        }
        finally
        {
            IsExporting = false;
        }
    }

    public bool TrySetOutputFolder(
        string outputFolder,
        out string? error
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            _outputSettings.SetOutputFolder(outputFolder);
            OnPropertyChanged(nameof(OutputFolder));
            OnPropertyChanged(
                nameof(ShowSpotifyLocalFilesHint)
            );
            StatusMessage = string.Empty;
            error = null;
            return true;
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            error = string.Format(
                CultureInfo.CurrentCulture,
                AppStrings.FolderChangeFailed,
                exception.Message
            );
            StatusMessage = error;
            return false;
        }
    }

    public void DismissSpotifyLocalFilesHint()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            _outputSettings.DismissSpotifyLocalFilesHint();
            OnPropertyChanged(
                nameof(ShowSpotifyLocalFilesHint)
            );
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                AppStrings.SpotifyHintDismissFailed,
                exception.Message
            );
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _lifetimeCancellation.Cancel();
        _previewPlayer.PositionChanged -= OnPreviewPositionChanged;
        _previewPlayer.PlaybackStopped -= OnPreviewStopped;
        _previewPlayer.Dispose();
        _lifetimeCancellation.Dispose();
        _disposed = true;
    }

    private AudioBufferSnapshot? CreateSelectionSnapshot()
    {
        var selection = CreateRawSelectionSnapshot();
        if (selection is null)
        {
            return null;
        }

        if (_separatedStemSet is not null)
        {
            selection = StemRemixer.Mix(
                _separatedStemSet,
                _stemVolumes.ToDictionary(
                    stem => stem.Kind,
                    stem => stem.VolumePercent / 100d
                ),
                _lifetimeCancellation.Token
            );
        }

        return ClipEffectsProcessor.Process(
            selection,
            new ClipEffectSettings
            {
                SpeedMultiplier = SpeedMultiplier,
                EqualizerCurve = _equalizerSelection.Curve,
                ReverbWetMix =
                    ReverbAmountPercent / 100,
                LoopCount = LoopCount,
                RotationRateHertz = RotationRateHertz,
            },
            _lifetimeCancellation.Token
        );
    }

    private AudioBufferSnapshot? CreateRawSelectionSnapshot()
    {
        if (
            SelectionStart is not { } start
            || SelectionEnd is not { } end
        )
        {
            StatusMessage = AppStrings.SelectFirst;
            return null;
        }

        return AudioSnapshotSlicer.Slice(
            Snapshot,
            start,
            end
        );
    }

    private void OnEqualizerBandChanged(
        int bandIndex,
        double gainDecibels
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var selection = _equalizerSelection.AdjustBand(
            bandIndex,
            gainDecibels
        );
        if (selection.Equals(_equalizerSelection))
        {
            return;
        }

        _equalizerSelection = selection;
        EffectsChanged(
            nameof(EqualizerPreset),
            nameof(EqualizerPresetText)
        );
    }

    private void OnStemVolumeChanged()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EffectsChanged();
    }

    private void ResetStemMix()
    {
        if (_separatedStemSet is null && _stemVolumes.Count == 0)
        {
            return;
        }

        _separatedStemSet = null;
        _stemVolumes = Array.Empty<StemVolumeViewModel>();
        OnPropertyChanged(nameof(StemVolumes));
        OnPropertyChanged(nameof(HasSeparatedStems));
    }

    private static IEnumerable<SeparatedStem> OrderStemsForDisplay(
        SeparatedStemSet result
    )
    {
        StemKind[] displayOrder =
        [
            StemKind.Vocals,
            StemKind.Bass,
            StemKind.Drums,
            StemKind.Other,
            StemKind.Guitar,
            StemKind.Piano,
        ];
        return displayOrder
            .Select(
                kind =>
                    result.Stems.FirstOrDefault(
                        stem => stem.Kind == kind
                    )
            )
            .Where(stem => stem is not null)
            .Select(stem => stem!);
    }

    private void EffectsChanged(params string[] propertyNames)
    {
        var restartPreview = _previewPlayer.IsPlaying;
        StopPreview();
        foreach (var propertyName in propertyNames)
        {
            OnPropertyChanged(propertyName);
        }

        StatusMessage = string.Empty;
        if (restartPreview)
        {
            StartPreview();
        }
    }

    private void StopPreview()
    {
        if (_previewPlayer.IsPlaying)
        {
            _previewPlayer.Stop();
        }

        Playhead = null;
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PreviewButtonText));
    }

    public void ClearSelection()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        StopPreview();
        CancelAndResetStemMix();
        SelectionStart = null;
        SelectionEnd = null;
        NotifySelectionProperties();
    }

    private void NotifySelectionProperties()
    {
        OnPropertyChanged(nameof(SelectionStartText));
        OnPropertyChanged(nameof(SelectionEndText));
        OnPropertyChanged(nameof(SelectionDurationText));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionOverlapsExcluded));
        OnPropertyChanged(nameof(CanPreview));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanIsolateStems));
    }

    private void CancelAndResetStemMix()
    {
        _stemCancellation?.Cancel();
        ResetStemMix();
    }

    private void OnPreviewPositionChanged(
        object? sender,
        AudioPreviewPositionChangedEventArgs args
    ) => Playhead = args.Position;

    private void OnPreviewStopped(object? sender, EventArgs args)
    {
        Playhead = null;
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PreviewButtonText));
    }

    private static string FormatTime(TimeSpan? value)
    {
        if (value is null)
        {
            return AppStrings.EmptyTime;
        }

        var time = value.Value;
        var hours = (int)time.TotalHours;
        return hours > 0
            ? string.Create(
                CultureInfo.CurrentCulture,
                $"{hours}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds / 100}"
            )
            : string.Create(
                CultureInfo.CurrentCulture,
                $"{(int)time.TotalMinutes}:{time.Seconds:00}.{time.Milliseconds / 100}"
            );
    }

    private static string FormatDuration(TimeSpan value) =>
        string.Create(
            CultureInfo.CurrentCulture,
            $"{value.TotalSeconds:0.0}s"
        );

    private static TimeSpan Clamp(
        TimeSpan value,
        TimeSpan minimum,
        TimeSpan maximum
    ) =>
        value < minimum
            ? minimum
            : value > maximum
                ? maximum
                : value;

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null
    ) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName)
        );
}
