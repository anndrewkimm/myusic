using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Hookline.Audio;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace Hookline.App;

public sealed class StemBandAvatarControl : FrameworkElement
{
    private const double AvatarHalfHeight = 18;
    private const double TrackInset = AvatarHalfHeight + 4;

    private static readonly Brush LaneBrush = Freeze(
        new SolidColorBrush(Color.FromRgb(25, 31, 42))
    );
    private static readonly Brush ShadowBrush = Freeze(
        new SolidColorBrush(Color.FromArgb(70, 0, 0, 0))
    );
    private static readonly Brush FaceBrush = Freeze(
        new SolidColorBrush(Color.FromRgb(244, 211, 178))
    );
    private static readonly Brush DetailBrush = Freeze(
        new SolidColorBrush(Color.FromRgb(13, 16, 22))
    );
    private static readonly Pen LanePen = Freeze(
        new Pen(
            new SolidColorBrush(Color.FromRgb(63, 73, 91)),
            2
        )
    );
    private static readonly Pen NaturalPositionPen = Freeze(
        CreateDashedPen(
            Color.FromArgb(150, 91, 231, 177),
            1
        )
    );
    private static readonly Pen DetailThinPen = Freeze(
        new Pen(DetailBrush, 1)
    );
    private static readonly Pen DetailMediumPen = Freeze(
        new Pen(DetailBrush, 1.5)
    );
    private static readonly Pen DetailStrongPen = Freeze(
        new Pen(DetailBrush, 2)
    );
    private static readonly Pen DetailHeavyPen = Freeze(
        new Pen(DetailBrush, 3)
    );
    private static readonly Brush VocalsBrush = Freeze(
        new SolidColorBrush(Color.FromRgb(240, 112, 161))
    );
    private static readonly Brush BassBrush = Freeze(
        new SolidColorBrush(Color.FromRgb(102, 166, 255))
    );
    private static readonly Brush DrumsBrush = Freeze(
        new SolidColorBrush(Color.FromRgb(255, 174, 92))
    );
    private static readonly Brush OtherBrush = Freeze(
        new SolidColorBrush(Color.FromRgb(175, 133, 255))
    );
    private static readonly Brush GuitarBrush = Freeze(
        new SolidColorBrush(Color.FromRgb(91, 231, 177))
    );
    private static readonly Brush PianoBrush = Freeze(
        new SolidColorBrush(Color.FromRgb(242, 214, 101))
    );

    public static readonly DependencyProperty StemKindProperty =
        DependencyProperty.Register(
            nameof(StemKind),
            typeof(StemKind),
            typeof(StemBandAvatarControl),
            new FrameworkPropertyMetadata(
                StemKind.Other,
                FrameworkPropertyMetadataOptions.AffectsRender
            )
        );

    public static readonly DependencyProperty VolumePercentProperty =
        DependencyProperty.Register(
            nameof(VolumePercent),
            typeof(double),
            typeof(StemBandAvatarControl),
            new FrameworkPropertyMetadata(
                100d,
                FrameworkPropertyMetadataOptions.AffectsRender
                    | FrameworkPropertyMetadataOptions
                        .BindsTwoWayByDefault,
                null,
                CoerceVolumePercent
            )
        );

    private bool _isDragging;

    public StemBandAvatarControl()
    {
        Cursor = Cursors.SizeNS;
        Focusable = false;
        SnapsToDevicePixels = true;
    }

    public StemKind StemKind
    {
        get => (StemKind)GetValue(StemKindProperty);
        set => SetValue(StemKindProperty, value);
    }

    public double VolumePercent
    {
        get => (double)GetValue(VolumePercentProperty);
        set => SetValue(VolumePercentProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var centerX = ActualWidth / 2;
        var (trackTop, trackLength) = GetTrack();
        var laneBounds = new Rect(
            centerX - 18,
            trackTop - 5,
            36,
            trackLength + 10
        );

        drawingContext.PushOpacity(IsEnabled ? 1 : 0.42);
        drawingContext.DrawRoundedRectangle(
            LaneBrush,
            null,
            laneBounds,
            18,
            18
        );
        drawingContext.DrawLine(
            LanePen,
            new Point(centerX, trackTop),
            new Point(centerX, trackTop + trackLength)
        );

        var naturalPosition =
            trackTop
            + StemBandPositionMapper.GainToPosition(
                100,
                trackLength
            );
        drawingContext.DrawLine(
            NaturalPositionPen,
            new Point(centerX - 23, naturalPosition),
            new Point(centerX + 23, naturalPosition)
        );

        var avatarPosition =
            trackTop
            + StemBandPositionMapper.GainToPosition(
                VolumePercent,
                trackLength
            );
        DrawAvatar(
            drawingContext,
            new Point(centerX, avatarPosition)
        );
        drawingContext.Pop();
    }

    protected override void OnMouseLeftButtonDown(
        MouseButtonEventArgs args
    )
    {
        base.OnMouseLeftButtonDown(args);
        if (!IsEnabled)
        {
            return;
        }

        _isDragging = CaptureMouse();
        UpdateVolume(args.GetPosition(this).Y);
        args.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);
        if (
            !_isDragging
            || args.LeftButton != MouseButtonState.Pressed
        )
        {
            return;
        }

        UpdateVolume(args.GetPosition(this).Y);
        args.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(
        MouseButtonEventArgs args
    )
    {
        base.OnMouseLeftButtonUp(args);
        if (!_isDragging)
        {
            return;
        }

        UpdateVolume(args.GetPosition(this).Y);
        _isDragging = false;
        ReleaseMouseCapture();
        args.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs args)
    {
        base.OnLostMouseCapture(args);
        _isDragging = false;
    }

    private void UpdateVolume(double pointerY)
    {
        var (trackTop, trackLength) = GetTrack();
        var gain = StemBandPositionMapper.PositionToGain(
            pointerY - trackTop,
            trackLength
        );
        SetCurrentValue(VolumePercentProperty, gain);
    }

    private (double Top, double Length) GetTrack()
    {
        var trackLength = Math.Max(
            1,
            ActualHeight - (TrackInset * 2)
        );
        return (TrackInset, trackLength);
    }

    private void DrawAvatar(
        DrawingContext drawingContext,
        Point center
    )
    {
        var accent = GetStemBrush(StemKind);
        drawingContext.DrawEllipse(
            ShadowBrush,
            null,
            new Point(center.X + 2, center.Y + 16),
            17,
            5
        );
        drawingContext.DrawEllipse(
            FaceBrush,
            null,
            new Point(center.X, center.Y - 9),
            6,
            6
        );

        switch (StemKind)
        {
            case StemKind.Vocals:
                DrawVocals(drawingContext, center, accent);
                break;
            case StemKind.Bass:
                DrawBass(drawingContext, center, accent);
                break;
            case StemKind.Drums:
                DrawDrums(drawingContext, center, accent);
                break;
            case StemKind.Guitar:
                DrawGuitar(drawingContext, center, accent);
                break;
            case StemKind.Piano:
                DrawPiano(drawingContext, center, accent);
                break;
            default:
                DrawOther(drawingContext, center, accent);
                break;
        }
    }

    private static void DrawVocals(
        DrawingContext drawingContext,
        Point center,
        Brush accent
    )
    {
        drawingContext.DrawRoundedRectangle(
            accent,
            null,
            new Rect(center.X - 8, center.Y - 2, 16, 18),
            7,
            7
        );
        drawingContext.DrawLine(
            DetailStrongPen,
            new Point(center.X + 10, center.Y),
            new Point(center.X + 14, center.Y + 12)
        );
        drawingContext.DrawEllipse(
            DetailBrush,
            null,
            new Point(center.X + 10, center.Y - 1),
            3,
            3
        );
    }

    private static void DrawBass(
        DrawingContext drawingContext,
        Point center,
        Brush accent
    )
    {
        drawingContext.DrawRoundedRectangle(
            accent,
            null,
            new Rect(center.X - 8, center.Y - 2, 16, 18),
            4,
            4
        );
        drawingContext.DrawEllipse(
            DetailBrush,
            null,
            new Point(center.X + 2, center.Y + 6),
            8,
            5
        );
        drawingContext.DrawLine(
            DetailHeavyPen,
            new Point(center.X + 8, center.Y + 4),
            new Point(center.X + 17, center.Y - 2)
        );
    }

    private static void DrawDrums(
        DrawingContext drawingContext,
        Point center,
        Brush accent
    )
    {
        drawingContext.DrawRoundedRectangle(
            accent,
            null,
            new Rect(center.X - 7, center.Y - 2, 14, 12),
            5,
            5
        );
        drawingContext.DrawEllipse(
            accent,
            DetailStrongPen,
            new Point(center.X, center.Y + 9),
            13,
            8
        );
        drawingContext.DrawLine(
            DetailMediumPen,
            new Point(center.X - 10, center.Y - 4),
            new Point(center.X + 7, center.Y + 8)
        );
        drawingContext.DrawLine(
            DetailMediumPen,
            new Point(center.X + 10, center.Y - 4),
            new Point(center.X - 7, center.Y + 8)
        );
    }

    private static void DrawOther(
        DrawingContext drawingContext,
        Point center,
        Brush accent
    )
    {
        var diamond = new StreamGeometry();
        using (var context = diamond.Open())
        {
            context.BeginFigure(
                new Point(center.X, center.Y - 2),
                true,
                true
            );
            context.LineTo(
                new Point(center.X + 11, center.Y + 7),
                true,
                false
            );
            context.LineTo(
                new Point(center.X, center.Y + 17),
                true,
                false
            );
            context.LineTo(
                new Point(center.X - 11, center.Y + 7),
                true,
                false
            );
        }

        diamond.Freeze();
        drawingContext.DrawGeometry(accent, null, diamond);
        drawingContext.DrawEllipse(
            DetailBrush,
            null,
            new Point(center.X, center.Y + 7),
            3,
            3
        );
    }

    private static void DrawGuitar(
        DrawingContext drawingContext,
        Point center,
        Brush accent
    )
    {
        drawingContext.DrawRoundedRectangle(
            accent,
            null,
            new Rect(center.X - 7, center.Y - 2, 14, 16),
            5,
            5
        );
        drawingContext.DrawEllipse(
            DetailBrush,
            null,
            new Point(center.X - 1, center.Y + 8),
            7,
            6
        );
        drawingContext.DrawLine(
            DetailHeavyPen,
            new Point(center.X + 4, center.Y + 4),
            new Point(center.X + 16, center.Y - 5)
        );
    }

    private static void DrawPiano(
        DrawingContext drawingContext,
        Point center,
        Brush accent
    )
    {
        drawingContext.DrawRoundedRectangle(
            accent,
            null,
            new Rect(center.X - 8, center.Y - 2, 16, 13),
            4,
            4
        );
        drawingContext.DrawRectangle(
            accent,
            DetailStrongPen,
            new Rect(center.X - 16, center.Y + 6, 32, 11)
        );
        for (var key = -10; key <= 10; key += 5)
        {
            drawingContext.DrawLine(
                DetailThinPen,
                new Point(center.X + key, center.Y + 7),
                new Point(center.X + key, center.Y + 16)
            );
        }
    }

    private static Brush GetStemBrush(StemKind kind) =>
        kind switch
        {
            StemKind.Vocals => VocalsBrush,
            StemKind.Bass => BassBrush,
            StemKind.Drums => DrumsBrush,
            StemKind.Guitar => GuitarBrush,
            StemKind.Piano => PianoBrush,
            _ => OtherBrush,
        };

    private static object CoerceVolumePercent(
        DependencyObject dependencyObject,
        object baseValue
    )
    {
        _ = dependencyObject;
        var value = (double)baseValue;
        return double.IsFinite(value)
            ? Math.Clamp(
                value,
                StemBandPositionMapper.MinimumGainPercent,
                StemBandPositionMapper.MaximumGainPercent
            )
            : 100d;
    }

    private static Pen CreateDashedPen(
        Color color,
        double thickness
    )
    {
        var pen = new Pen(
            new SolidColorBrush(color),
            thickness
        )
        {
            DashStyle = DashStyles.Dash,
        };
        return pen;
    }

    private static T Freeze<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
