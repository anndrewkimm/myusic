using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Hookline.Audio;

namespace Hookline.App;

internal sealed class UrlImportViewModel :
    INotifyPropertyChanged,
    IDisposable
{
    private readonly IUrlAudioImportService _importService;
    private CancellationTokenSource? _operationCancellation;
    private string _url = string.Empty;
    private UrlVideoMetadata? _preview;
    private string _statusMessage = AppStrings.UrlImportInitial;
    private double _progressPercent;
    private bool _isResolving;
    private bool _isDownloading;
    private bool _isNoticeVisible;
    private bool _disposed;

    public UrlImportViewModel(
        IUrlAudioImportService importService,
        bool showPersonalUseNotice
    )
    {
        ArgumentNullException.ThrowIfNull(importService);
        _importService = importService;
        _isNoticeVisible = showPersonalUseNotice;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Url
    {
        get => _url;
        set
        {
            if (string.Equals(_url, value, StringComparison.Ordinal))
            {
                return;
            }

            _url = value;
            ClearPreview();
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanFetch));
        }
    }

    public string Title => _preview?.Title ?? string.Empty;

    public string Channel => _preview?.Channel ?? string.Empty;

    public string DurationText => _preview is null
        ? string.Empty
        : string.Format(
            CultureInfo.CurrentCulture,
            AppStrings.UrlImportDuration,
            FormatDuration(_preview.Duration)
        );

    public byte[] Thumbnail =>
        _preview?.Thumbnail.ToArray() ?? [];

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetField(ref _progressPercent, value);
    }

    public bool IsResolving
    {
        get => _isResolving;
        private set => SetOperationState(ref _isResolving, value);
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        private set => SetOperationState(ref _isDownloading, value);
    }

    public bool IsBusy => IsResolving || IsDownloading;

    public bool HasPreview => _preview is not null;

    public bool CanFetch =>
        !IsBusy && !string.IsNullOrWhiteSpace(Url);

    public bool CanImport => !IsBusy && HasPreview;

    public bool IsNoticeVisible
    {
        get => _isNoticeVisible;
        private set => SetField(ref _isNoticeVisible, value);
    }

    public async Task ResolveAsync()
    {
        if (!CanFetch)
        {
            return;
        }

        var cancellation = BeginOperation();
        ClearPreview();
        IsResolving = true;
        StatusMessage = AppStrings.UrlImportResolving;
        try
        {
            _preview = await _importService.ResolveAsync(
                Url,
                cancellation.Token
            );
            StatusMessage = AppStrings.UrlImportReady;
            NotifyPreviewChanged();
        }
        catch (OperationCanceledException)
            when (cancellation.IsCancellationRequested)
        {
            ReturnToInitialState();
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
            ClearPreview();
        }
        finally
        {
            IsResolving = false;
            EndOperation(cancellation);
        }
    }

    public async Task<ImportedAudioFile?> ImportAsync()
    {
        if (!CanImport || _preview is not { } preview)
        {
            return null;
        }

        var cancellation = BeginOperation();
        IsDownloading = true;
        ProgressPercent = 0;
        StatusMessage = string.Format(
            CultureInfo.CurrentCulture,
            AppStrings.UrlImportDownloading,
            0
        );
        var progress = new Progress<double>(value =>
        {
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            ProgressPercent = Math.Clamp(value * 100d, 0d, 100d);
            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                AppStrings.UrlImportDownloading,
                Math.Round(ProgressPercent)
            );
        });

        try
        {
            return await _importService.ImportAsync(
                preview,
                progress,
                cancellation.Token
            );
        }
        catch (OperationCanceledException)
            when (cancellation.IsCancellationRequested)
        {
            ReturnToInitialState();
            return null;
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
            return null;
        }
        finally
        {
            IsDownloading = false;
            EndOperation(cancellation);
        }
    }

    public void Cancel() => _operationCancellation?.Cancel();

    public void DismissNotice() => IsNoticeVisible = false;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        _disposed = true;
    }

    private CancellationTokenSource BeginOperation()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        return cancellation;
    }

    private void EndOperation(CancellationTokenSource cancellation)
    {
        if (ReferenceEquals(_operationCancellation, cancellation))
        {
            _operationCancellation = null;
        }

        cancellation.Dispose();
    }

    private void ReturnToInitialState()
    {
        ClearPreview();
        ProgressPercent = 0;
        StatusMessage = AppStrings.UrlImportCanceled;
    }

    private void ClearPreview()
    {
        if (_preview is null)
        {
            return;
        }

        _preview = null;
        NotifyPreviewChanged();
    }

    private void NotifyPreviewChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Channel));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(Thumbnail));
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(CanImport));
    }

    private void SetOperationState(ref bool field, bool value)
    {
        if (!SetField(ref field, value))
        {
            return;
        }

        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanFetch));
        OnPropertyChanged(nameof(CanImport));
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null
    )
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null
    ) => PropertyChanged?.Invoke(
        this,
        new PropertyChangedEventArgs(propertyName)
    );

    private static string FormatDuration(TimeSpan duration) =>
        duration <= TimeSpan.Zero
            ? AppStrings.EmptyTime
            : duration.TotalHours >= 1
                ? duration.ToString(@"h\:mm\:ss", CultureInfo.CurrentCulture)
                : duration.ToString(@"m\:ss", CultureInfo.CurrentCulture);
}
