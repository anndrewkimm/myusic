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
    public static readonly TimeSpan MinimumSegmentDuration =
        TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan SegmentPreviewDebounce =
        TimeSpan.FromMilliseconds(300);

    private readonly IClipExporter _exporter;
    private readonly IAudioPreviewPlayer _previewPlayer;
    private readonly OutputFolderSettings _outputSettings;
    private readonly IStemIsolationService _stemIsolationService;
    private readonly CancellationTokenSource _lifetimeCancellation =
        new();
    private CancellationTokenSource? _stemCancellation;
    private CancellationTokenSource? _previewRenderCancellation;

    private TimeSpan? _selectionStart;
    private TimeSpan? _selectionEnd;
    private AudioBufferSnapshot? _rawSelectionSnapshot;
    private TimeSpan? _playhead;
    private TimeSpan _activePreviewDuration;
    private SelectionEdge _activeEdge = SelectionEdge.End;
    private string _statusMessage = string.Empty;
    private readonly List<TimeSpan> _splitPoints = [];
    private readonly List<ClipSegmentEffectState> _segmentEffects =
        [new ClipSegmentEffectState()];
    private int _activeSegmentIndex;
    private bool _previewRestartPending;
    private TimeSpan _pendingPreviewPosition;
    private TimeSpan _pendingPreviewDuration;
    private readonly IReadOnlyList<EqualizerBandViewModel>
        _equalizerBands;
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

    private ClipSegmentEffectState ActiveSegment =>
        _segmentEffects[_activeSegmentIndex];

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
            _rawSelectionSnapshot = null;
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
            _rawSelectionSnapshot = null;
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

    public IReadOnlyList<TimeSpan> SplitPoints => _splitPoints;

    public int ActiveSegmentIndex => _activeSegmentIndex;

    public int SegmentCount => _segmentEffects.Count;

    public string ActiveSegmentText =>
        string.Format(
            CultureInfo.CurrentCulture,
            AppStrings.ActiveSegment,
            ActiveSegmentIndex + 1,
            SegmentCount
        );

    public TimeSpan ExportDuration => CalculateExportDuration();

    public bool HasAdjustedExportDuration =>
        TryCreateRawSelectionSnapshot() is { } raw
        && ExportDuration != raw.Duration;

    public string ExportDurationText =>
        string.Format(
            CultureInfo.CurrentCulture,
            AppStrings.AdjustedExportDuration,
            FormatDuration(ExportDuration)
        );

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
        get => ActiveSegment.EditEffectSelection.SpeedMultiplier;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var normalized = Math.Clamp(
                Math.Round(value, 2),
                ClipEffectSettings.MinimumSpeedMultiplier,
                ClipEffectSettings.MaximumSpeedMultiplier
            );
            var selection =
                ActiveSegment.EditEffectSelection.AdjustSpeed(normalized);
            if (selection.Equals(ActiveSegment.EditEffectSelection))
            {
                return;
            }

            ActiveSegment.EditEffectSelection = selection;
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
            ActiveSegment.EditEffectSelection.ReverbWetMix * 100
        );
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var normalized = Math.Clamp(
                Math.Round(value),
                ClipEffectSettings.MinimumReverbWetMix * 100,
                ClipEffectSettings.MaximumReverbWetMix * 100
            );
            var selection = ActiveSegment.EditEffectSelection.AdjustReverb(
                normalized / 100
            );
            if (selection.Equals(ActiveSegment.EditEffectSelection))
            {
                return;
            }

            ActiveSegment.EditEffectSelection = selection;
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
        get => ActiveSegment.EditEffectSelection.RotationRateHertz;
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
                ActiveSegment.EditEffectSelection.AdjustRotation(normalized);
            if (selection.Equals(ActiveSegment.EditEffectSelection))
            {
                return;
            }

            ActiveSegment.EditEffectSelection = selection;
            EffectsChanged(
                nameof(RotationRateHertz),
                nameof(RotationRateText),
                nameof(EditEffectPreset),
                nameof(EditEffectPresetText)
            );
        }
    }

    public EditEffectPreset EditEffectPreset =>
        ActiveSegment.EditEffectSelection.Preset;

    public string EditEffectPresetText =>
        ActiveSegment.EditEffectSelection.Preset switch
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
        ActiveSegment.EqualizerSelection.Preset;

    public string EqualizerPresetText =>
        ActiveSegment.EqualizerSelection.Preset switch
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
        get => ActiveSegment.LoopCount;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var normalized = Math.Clamp(
                value,
                ClipEffectSettings.MinimumLoopCount,
                ClipEffectSettings.MaximumLoopCount
            );
            if (ActiveSegment.LoopCount == normalized)
            {
                return;
            }

            ActiveSegment.LoopCount = normalized;
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
        if (selection.Equals(ActiveSegment.EditEffectSelection))
        {
            return;
        }

        ActiveSegment.EditEffectSelection = selection;
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
        if (selection.Equals(ActiveSegment.EqualizerSelection))
        {
            return;
        }

        ActiveSegment.EqualizerSelection = selection;
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
            RebuildStemVolumes();
            StemProgressPercent = 100;
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

        if (_splitPoints.Count > 0)
        {
            clampedStart = Min(
                clampedStart,
                _splitPoints[0] - MinimumSegmentDuration
            );
            clampedEnd = Max(
                clampedEnd,
                _splitPoints[^1] + MinimumSegmentDuration
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

    public bool AddSplit(TimeSpan position)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (
            SelectionStart is not { } selectionStart
            || SelectionEnd is not { } selectionEnd
        )
        {
            return false;
        }

        var segmentIndex = FindSegmentIndex(position);
        var segmentStart = segmentIndex == 0
            ? selectionStart
            : _splitPoints[segmentIndex - 1];
        var segmentEnd = segmentIndex == _splitPoints.Count
            ? selectionEnd
            : _splitPoints[segmentIndex];
        if (
            position - segmentStart < MinimumSegmentDuration
            || segmentEnd - position < MinimumSegmentDuration
        )
        {
            return false;
        }

        var inherited = _segmentEffects[segmentIndex].Clone();
        _splitPoints.Insert(segmentIndex, position);
        _segmentEffects.Insert(segmentIndex + 1, inherited);
        _activeSegmentIndex = segmentIndex + 1;
        RefreshActiveSegmentControls();
        SegmentStructureChanged();
        return true;
    }

    public TimeSpan MoveSplit(int splitIndex, TimeSpan position)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(splitIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            splitIndex,
            _splitPoints.Count
        );
        if (
            SelectionStart is not { } selectionStart
            || SelectionEnd is not { } selectionEnd
        )
        {
            return _splitPoints[splitIndex];
        }

        var minimum =
            (splitIndex == 0
                ? selectionStart
                : _splitPoints[splitIndex - 1])
            + MinimumSegmentDuration;
        var maximum =
            (splitIndex == _splitPoints.Count - 1
                ? selectionEnd
                : _splitPoints[splitIndex + 1])
            - MinimumSegmentDuration;
        var clamped = Clamp(position, minimum, maximum);
        if (_splitPoints[splitIndex] == clamped)
        {
            return clamped;
        }

        _splitPoints[splitIndex] = clamped;
        SegmentStructureChanged();
        return clamped;
    }

    public bool RemoveSplit(int splitIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (splitIndex < 0 || splitIndex >= _splitPoints.Count)
        {
            return false;
        }

        _splitPoints.RemoveAt(splitIndex);
        _segmentEffects.RemoveAt(splitIndex + 1);
        _activeSegmentIndex = Math.Min(
            splitIndex,
            _segmentEffects.Count - 1
        );
        RefreshActiveSegmentControls();
        SegmentStructureChanged();
        return true;
    }

    public void SetActiveSegment(int segmentIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (
            segmentIndex < 0
            || segmentIndex >= _segmentEffects.Count
            || _activeSegmentIndex == segmentIndex
        )
        {
            return;
        }

        _activeSegmentIndex = segmentIndex;
        RefreshActiveSegmentControls();
        NotifyActiveSegmentProperties();
    }

    public void ResetSplits()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_splitPoints.Count == 0)
        {
            return;
        }

        var preserved = ActiveSegment.Clone();
        _splitPoints.Clear();
        _segmentEffects.Clear();
        _segmentEffects.Add(preserved);
        _activeSegmentIndex = 0;
        RefreshActiveSegmentControls();
        SegmentStructureChanged();
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
            var maximum = _splitPoints.Count == 0
                ? end - MinimumSelection
                : _splitPoints[0] - MinimumSegmentDuration;
            SelectionStart = Clamp(
                start + change,
                DisplayStart,
                maximum
            );
        }
        else
        {
            var minimum = _splitPoints.Count == 0
                ? start + MinimumSelection
                : _splitPoints[^1] + MinimumSegmentDuration;
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
        if (_previewPlayer.IsPlaying || _previewRestartPending)
        {
            StopPreview();
            return;
        }

        StartPreview();
    }

    private void StartPreview(
        TimeSpan previousPosition = default,
        TimeSpan previousDuration = default
    )
    {
        try
        {
            var selection = CreateSelectionSnapshot(
                _lifetimeCancellation.Token
            );
            if (selection is null)
            {
                return;
            }

            if (selection.Audio.IsEmpty)
            {
                StatusMessage = AppStrings.SelectionHasNoAudio;
                return;
            }

            PlayPreviewSnapshot(
                selection,
                previousPosition,
                previousDuration
            );
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

    private void PlayPreviewSnapshot(
        AudioBufferSnapshot selection,
        TimeSpan previousPosition,
        TimeSpan previousDuration
    )
    {
        var resumeAt = PreviewResumePositionMapper.Map(
            previousPosition,
            previousDuration,
            selection.Duration
        );
        _previewPlayer.Play(selection, resumeAt);
        _activePreviewDuration = selection.Duration;
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PreviewButtonText));
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
            var segmentedRequest = _splitPoints.Count == 0
                ? null
                : CreateSegmentedRenderRequest();
            var selection = _splitPoints.Count == 0
                ? CreateSelectionSnapshot(
                    _lifetimeCancellation.Token
                )
                : await Task.Run(
                    () => RenderSegmentedRequest(
                        segmentedRequest!,
                        _lifetimeCancellation.Token
                    ),
                    _lifetimeCancellation.Token
                );
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

        CancelPendingPreviewRender();
        _lifetimeCancellation.Cancel();
        _previewPlayer.PositionChanged -= OnPreviewPositionChanged;
        _previewPlayer.PlaybackStopped -= OnPreviewStopped;
        _previewPlayer.Dispose();
        _lifetimeCancellation.Dispose();
        _disposed = true;
    }

    private AudioBufferSnapshot? CreateSelectionSnapshot(
        CancellationToken cancellationToken = default
    )
    {
        var selection = CreateRawSelectionSnapshot();
        if (selection is null)
        {
            return null;
        }

        var segmentSettings = CreateSegmentRenderSettings();
        if (_splitPoints.Count > 0)
        {
            return SegmentedClipRenderer.Render(
                selection,
                segmentSettings,
                _separatedStemSet,
                cancellationToken
            );
        }

        if (_separatedStemSet is not null)
        {
            selection = StemRemixer.Mix(
                _separatedStemSet,
                segmentSettings[0].StemGains,
                cancellationToken
            );
        }

        return ClipEffectsProcessor.Process(
            selection,
            segmentSettings[0].Effects,
            cancellationToken
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

        return TryCreateRawSelectionSnapshot();
    }

    private AudioBufferSnapshot? TryCreateRawSelectionSnapshot()
    {
        if (
            SelectionStart is not { } start
            || SelectionEnd is not { } end
        )
        {
            return null;
        }

        _rawSelectionSnapshot ??= AudioSnapshotSlicer.Slice(
            Snapshot,
            start,
            end
        );
        return _rawSelectionSnapshot;
    }

    private IReadOnlyList<ClipSegmentRenderSettings>
        CreateSegmentRenderSettings()
    {
        if (
            SelectionStart is not { } selectionStart
            || SelectionEnd is not { } selectionEnd
        )
        {
            return Array.Empty<ClipSegmentRenderSettings>();
        }

        var settings = new ClipSegmentRenderSettings[
            _segmentEffects.Count
        ];
        var start = selectionStart;
        for (var index = 0; index < settings.Length; index++)
        {
            var end = index < _splitPoints.Count
                ? _splitPoints[index]
                : selectionEnd;
            settings[index] = _segmentEffects[index]
                .CreateRenderSettings(start, end);
            start = end;
        }

        return settings;
    }

    private TimeSpan CalculateExportDuration()
    {
        var raw = TryCreateRawSelectionSnapshot();
        if (raw is null)
        {
            return TimeSpan.Zero;
        }

        var settings = CreateSegmentRenderSettings();
        return _splitPoints.Count == 0
            ? ClipEffectsProcessor.GetOutputDuration(
                raw,
                settings[0].Effects
            )
            : SegmentedClipRenderer.GetOutputDuration(raw, settings);
    }

    private SegmentedRenderRequest? CreateSegmentedRenderRequest()
    {
        var raw = TryCreateRawSelectionSnapshot();
        if (raw is null)
        {
            return null;
        }

        return new SegmentedRenderRequest(
            raw,
            CreateSegmentRenderSettings().ToArray(),
            _separatedStemSet
        );
    }

    private static AudioBufferSnapshot RenderSegmentedRequest(
        SegmentedRenderRequest request,
        CancellationToken cancellationToken
    ) =>
        SegmentedClipRenderer.Render(
            request.Source,
            request.Segments,
            request.SeparatedStems,
            cancellationToken
        );

    private void OnEqualizerBandChanged(
        int bandIndex,
        double gainDecibels
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var selection = ActiveSegment.EqualizerSelection.AdjustBand(
            bandIndex,
            gainDecibels
        );
        if (selection.Equals(ActiveSegment.EqualizerSelection))
        {
            return;
        }

        ActiveSegment.EqualizerSelection = selection;
        EffectsChanged(
            nameof(EqualizerPreset),
            nameof(EqualizerPresetText)
        );
    }

    private void OnStemVolumeChanged(
        StemKind kind,
        double volumePercent
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ActiveSegment.SetStemVolumePercent(kind, volumePercent);
        EffectsChanged();
    }

    private void ResetStemMix()
    {
        _separatedStemSet = null;
        foreach (var segment in _segmentEffects)
        {
            segment.ClearStemVolumes();
        }

        _stemVolumes = Array.Empty<StemVolumeViewModel>();
        OnPropertyChanged(nameof(StemVolumes));
        OnPropertyChanged(nameof(HasSeparatedStems));
    }

    private void RebuildStemVolumes()
    {
        _stemVolumes = _separatedStemSet is null
            ? Array.Empty<StemVolumeViewModel>()
            : OrderStemsForDisplay(_separatedStemSet)
                .Select(
                    stem =>
                        new StemVolumeViewModel(
                            stem.Kind,
                            OnStemVolumeChanged,
                            ActiveSegment.GetStemVolumePercent(
                                stem.Kind
                            )
                        )
                )
                .ToArray();
        OnPropertyChanged(nameof(StemVolumes));
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

    private int FindSegmentIndex(TimeSpan position)
    {
        for (var index = 0; index < _splitPoints.Count; index++)
        {
            if (position < _splitPoints[index])
            {
                return index;
            }
        }

        return _splitPoints.Count;
    }

    private void RefreshActiveSegmentControls()
    {
        for (var index = 0; index < _equalizerBands.Count; index++)
        {
            _equalizerBands[index].SetFromPreset(
                ActiveSegment.EqualizerSelection.Curve[index]
            );
        }

        RebuildStemVolumes();
    }

    private void SegmentStructureChanged()
    {
        NotifyActiveSegmentProperties();
        EffectsChanged(
            nameof(SplitPoints),
            nameof(ActiveSegmentIndex),
            nameof(SegmentCount),
            nameof(ActiveSegmentText)
        );
    }

    private void NotifyActiveSegmentProperties()
    {
        string[] propertyNames =
        [
            nameof(ActiveSegmentIndex),
            nameof(SegmentCount),
            nameof(ActiveSegmentText),
            nameof(SpeedMultiplier),
            nameof(SpeedMultiplierText),
            nameof(ReverbAmountPercent),
            nameof(ReverbAmountText),
            nameof(RotationRateHertz),
            nameof(RotationRateText),
            nameof(EditEffectPreset),
            nameof(EditEffectPresetText),
            nameof(LoopCount),
            nameof(LoopCountText),
            nameof(EqualizerPreset),
            nameof(EqualizerPresetText),
            nameof(StemVolumes),
            nameof(ExportDuration),
            nameof(HasAdjustedExportDuration),
            nameof(ExportDurationText),
        ];
        foreach (var propertyName in propertyNames)
        {
            OnPropertyChanged(propertyName);
        }
    }

    private void EffectsChanged(params string[] propertyNames)
    {
        var restartPreview =
            _previewPlayer.IsPlaying || _previewRestartPending;
        var previousPosition = _previewRestartPending
            ? _pendingPreviewPosition
            : restartPreview
                ? _previewPlayer.CurrentAudioPosition
                : TimeSpan.Zero;
        var previousDuration = _previewRestartPending
            ? _pendingPreviewDuration
            : restartPreview
                ? _activePreviewDuration
                : TimeSpan.Zero;
        StopPreviewPlayback();
        foreach (var propertyName in propertyNames)
        {
            OnPropertyChanged(propertyName);
        }

        OnPropertyChanged(nameof(ExportDuration));
        OnPropertyChanged(nameof(HasAdjustedExportDuration));
        OnPropertyChanged(nameof(ExportDurationText));

        StatusMessage = string.Empty;
        if (restartPreview)
        {
            if (_splitPoints.Count == 0)
            {
                CancelPendingPreviewRender();
                StartPreview(previousPosition, previousDuration);
            }
            else
            {
                ScheduleSegmentPreviewRender(
                    previousPosition,
                    previousDuration
                );
            }
        }
    }

    private void StopPreview()
    {
        CancelPendingPreviewRender();
        StopPreviewPlayback();
    }

    private void StopPreviewPlayback()
    {
        if (_previewPlayer.IsPlaying)
        {
            _previewPlayer.Stop();
        }

        _activePreviewDuration = TimeSpan.Zero;
        Playhead = null;
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PreviewButtonText));
    }

    private void ScheduleSegmentPreviewRender(
        TimeSpan previousPosition,
        TimeSpan previousDuration
    )
    {
        CancelPendingPreviewRender();
        var request = CreateSegmentedRenderRequest();
        if (request is null)
        {
            return;
        }

        var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                _lifetimeCancellation.Token
            );
        _previewRenderCancellation = cancellation;
        _previewRestartPending = true;
        _pendingPreviewPosition = previousPosition;
        _pendingPreviewDuration = previousDuration;
        _ = RenderSegmentPreviewAfterDebounceAsync(
            cancellation,
            request,
            previousPosition,
            previousDuration
        );
    }

    private async Task RenderSegmentPreviewAfterDebounceAsync(
        CancellationTokenSource cancellation,
        SegmentedRenderRequest request,
        TimeSpan previousPosition,
        TimeSpan previousDuration
    )
    {
        try
        {
            await Task.Delay(
                SegmentPreviewDebounce,
                cancellation.Token
            );
            var selection = await Task.Run(
                () =>
                    RenderSegmentedRequest(
                        request,
                        cancellation.Token
                    ),
                cancellation.Token
            );
            cancellation.Token.ThrowIfCancellationRequested();
            if (selection is null)
            {
                return;
            }

            if (selection.Audio.IsEmpty)
            {
                StatusMessage = AppStrings.SelectionHasNoAudio;
                return;
            }

            _previewRestartPending = false;
            PlayPreviewSnapshot(
                selection,
                previousPosition,
                previousDuration
            );
        }
        catch (OperationCanceledException)
            when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (
                ReferenceEquals(
                    _previewRenderCancellation,
                    cancellation
                )
            )
            {
                _previewRestartPending = false;
                StatusMessage = string.Format(
                    CultureInfo.CurrentCulture,
                    AppStrings.PreviewFailed,
                    exception.Message
                );
            }
        }
        finally
        {
            if (
                ReferenceEquals(
                    _previewRenderCancellation,
                    cancellation
                )
            )
            {
                _previewRenderCancellation = null;
                _previewRestartPending = false;
            }

            cancellation.Dispose();
        }
    }

    private void CancelPendingPreviewRender()
    {
        _previewRestartPending = false;
        _previewRenderCancellation?.Cancel();
        _previewRenderCancellation = null;
    }

    public void ClearSelection()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        StopPreview();
        CancelAndResetStemMix();
        if (_splitPoints.Count > 0)
        {
            var preserved = ActiveSegment.Clone();
            _splitPoints.Clear();
            _segmentEffects.Clear();
            _segmentEffects.Add(preserved);
            _activeSegmentIndex = 0;
            RefreshActiveSegmentControls();
            OnPropertyChanged(nameof(SplitPoints));
            NotifyActiveSegmentProperties();
        }

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
        OnPropertyChanged(nameof(ExportDuration));
        OnPropertyChanged(nameof(HasAdjustedExportDuration));
        OnPropertyChanged(nameof(ExportDurationText));
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
        _activePreviewDuration = TimeSpan.Zero;
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

    private static TimeSpan Min(TimeSpan left, TimeSpan right) =>
        left <= right ? left : right;

    private static TimeSpan Max(TimeSpan left, TimeSpan right) =>
        left >= right ? left : right;

    private sealed record SegmentedRenderRequest(
        AudioBufferSnapshot Source,
        IReadOnlyList<ClipSegmentRenderSettings> Segments,
        SeparatedStemSet? SeparatedStems
    );

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null
    ) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName)
        );
}
