using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Hookline.Audio;

namespace Hookline.App;

public sealed class StemVolumeViewModel
    : INotifyPropertyChanged
{
    private readonly Action _changed;
    private double _volumePercent = 100;

    public StemVolumeViewModel(
        StemKind kind,
        Action changed
    )
    {
        Kind = kind;
        _changed =
            changed
            ?? throw new ArgumentNullException(nameof(changed));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public StemKind Kind { get; }

    public string Name =>
        Kind switch
        {
            StemKind.Vocals => AppStrings.StemVocals,
            StemKind.Bass => AppStrings.StemBass,
            StemKind.Drums => AppStrings.StemDrums,
            StemKind.Other => AppStrings.StemOther,
            StemKind.Guitar => AppStrings.StemGuitar,
            StemKind.Piano => AppStrings.StemPiano,
            _ => throw new InvalidOperationException(),
        };

    public double VolumePercent
    {
        get => _volumePercent;
        set
        {
            var normalized = Math.Clamp(
                Math.Round(value),
                StemRemixer.MinimumGain * 100,
                StemRemixer.MaximumGain * 100
            );
            if (_volumePercent == normalized)
            {
                return;
            }

            _volumePercent = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VolumeText));
            _changed();
        }
    }

    public string VolumeText =>
        string.Create(
            CultureInfo.CurrentCulture,
            $"{VolumePercent:0}%"
        );

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null
    ) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName)
        );
}
