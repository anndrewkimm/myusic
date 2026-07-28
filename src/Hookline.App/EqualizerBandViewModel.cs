using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Hookline.Audio;

namespace Hookline.App;

public sealed class EqualizerBandViewModel : INotifyPropertyChanged
{
    private readonly Action<int, double> _gainChanged;
    private double _gainDecibels;

    public EqualizerBandViewModel(
        int index,
        int centerFrequency,
        Action<int, double> gainChanged
    )
    {
        Index = index;
        CenterFrequency = centerFrequency;
        _gainChanged =
            gainChanged
            ?? throw new ArgumentNullException(nameof(gainChanged));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Index { get; }

    public int CenterFrequency { get; }

    public string FrequencyText =>
        CenterFrequency >= 1_000
            ? string.Create(
                CultureInfo.CurrentCulture,
                $"{CenterFrequency / 1_000}k"
            )
            : CenterFrequency.ToString(CultureInfo.CurrentCulture);

    public string Description =>
        string.Format(
            CultureInfo.CurrentCulture,
            AppStrings.EqualizerBandDescription,
            FrequencyText
        );

    public double GainDecibels
    {
        get => _gainDecibels;
        set
        {
            var normalized = Normalize(value);
            if (_gainDecibels == normalized)
            {
                return;
            }

            _gainDecibels = normalized;
            NotifyGainChanged();
            _gainChanged(Index, normalized);
        }
    }

    public string GainText =>
        string.Create(
            CultureInfo.CurrentCulture,
            $"{GainDecibels:+0;-0;0}"
        );

    internal void SetFromPreset(double gainDecibels)
    {
        var normalized = Normalize(gainDecibels);
        if (_gainDecibels == normalized)
        {
            return;
        }

        _gainDecibels = normalized;
        NotifyGainChanged();
    }

    private static double Normalize(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        return Math.Clamp(
            Math.Round(value),
            GraphicEqualizerBands.MinimumGainDecibels,
            GraphicEqualizerBands.MaximumGainDecibels
        );
    }

    private void NotifyGainChanged()
    {
        OnPropertyChanged(nameof(GainDecibels));
        OnPropertyChanged(nameof(GainText));
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null
    ) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName)
        );
}
