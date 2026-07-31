using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Hookline.Audio;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using FlowDirection = System.Windows.FlowDirection;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace Hookline.App;

public sealed class WaveformControl : FrameworkElement
{
    private const double HorizontalPadding = 14;
    private const double VerticalPadding = 20;
    private const double HandleHitWidth = 12;
    private const double SplitHitWidth = 9;
    private static readonly Brush BackgroundBrush =
        Freeze(new SolidColorBrush(Color.FromRgb(19, 23, 31)));
    private static readonly Brush SelectionBrush =
        Freeze(
            new SolidColorBrush(
                Color.FromArgb(42, 91, 231, 177)
            )
        );
    private static readonly Brush ActiveSegmentBrush =
        Freeze(
            new SolidColorBrush(
                Color.FromArgb(34, 91, 231, 177)
            )
        );
    private static readonly Brush ExcludedBrush =
        Freeze(
            new SolidColorBrush(
                Color.FromArgb(34, 255, 180, 92)
            )
        );
    private static readonly Pen WaveformPen =
        Freeze(
            new Pen(
                new SolidColorBrush(
                    Color.FromRgb(132, 144, 163)
                ),
                1
            )
        );
    private static readonly Pen SelectionEdgePen =
        Freeze(
            new Pen(
                new SolidColorBrush(
                    Color.FromRgb(91, 231, 177)
                ),
                2
            )
        );
    private static readonly Pen SplitPen =
        Freeze(
            new Pen(
                new SolidColorBrush(
                    Color.FromRgb(255, 191, 92)
                ),
                2
            )
        );
    private static readonly Pen NowPen =
        Freeze(
            new Pen(
                new SolidColorBrush(
                    Color.FromRgb(235, 241, 247)
                ),
                1
            )
        );
    private static readonly Pen HatchPen =
        Freeze(
            new Pen(
                new SolidColorBrush(
                    Color.FromArgb(120, 255, 180, 92)
                ),
                1
            )
        );

    private AudioBufferSnapshot? _snapshot;
    private TimeSpan? _selectionStart;
    private TimeSpan? _selectionEnd;
    private TimeSpan? _playhead;
    private TimeSpan _dragAnchor;
    private Point _mouseDownPoint;
    private readonly List<TimeSpan> _splitPoints = [];
    private int _activeSegmentIndex;
    private int _dragSplitIndex = -1;
    private DragMode _dragMode;

    public WaveformControl()
    {
        Focusable = true;
        Cursor = Cursors.Cross;
        SnapsToDevicePixels = true;
    }

    public event EventHandler<WaveformSelectionChangedEventArgs>?
        SelectionChanged;

    public event EventHandler<SelectionEdgeChangedEventArgs>?
        ActiveEdgeChanged;

    public event EventHandler<WaveformSplitRequestedEventArgs>?
        SplitRequested;

    public event EventHandler<WaveformSplitChangedEventArgs>?
        SplitChanged;

    public event EventHandler<WaveformSegmentActivatedEventArgs>?
        SegmentActivated;

    public event EventHandler? NewSelectionStarted;

    public AudioBufferSnapshot? Snapshot
    {
        get => _snapshot;
        set
        {
            _snapshot = value;
            InvalidateVisual();
        }
    }

    public TimeSpan? SelectionStart
    {
        get => _selectionStart;
        set
        {
            _selectionStart = value;
            InvalidateVisual();
        }
    }

    public TimeSpan? SelectionEnd
    {
        get => _selectionEnd;
        set
        {
            _selectionEnd = value;
            InvalidateVisual();
        }
    }

    public TimeSpan? Playhead
    {
        get => _playhead;
        set
        {
            _playhead = value;
            InvalidateVisual();
        }
    }

    public IReadOnlyList<TimeSpan> SplitPoints
    {
        get => _splitPoints;
        set
        {
            _splitPoints.Clear();
            if (value is not null)
            {
                _splitPoints.AddRange(value);
            }

            InvalidateVisual();
        }
    }

    public int ActiveSegmentIndex
    {
        get => _activeSegmentIndex;
        set
        {
            _activeSegmentIndex = Math.Clamp(
                value,
                0,
                _splitPoints.Count
            );
            InvalidateVisual();
        }
    }

    public TimeSpan MinimumSegmentDuration { get; set; } =
        TimeSpan.FromMilliseconds(250);

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var fullRect = new Rect(RenderSize);
        drawingContext.DrawRoundedRectangle(
            BackgroundBrush,
            null,
            fullRect,
            9,
            9
        );

        var content = new Rect(
            HorizontalPadding,
            VerticalPadding,
            Math.Max(0, ActualWidth - (HorizontalPadding * 2)),
            Math.Max(0, ActualHeight - (VerticalPadding * 2))
        );
        if (
            content.Width <= 0
            || content.Height <= 0
            || !TryGetTimeline(out var start, out var end)
        )
        {
            DrawEmptyState(drawingContext, fullRect);
            return;
        }

        drawingContext.PushClip(
            new RectangleGeometry(content, 5, 5)
        );
        DrawSelectionBackground(
            drawingContext,
            content,
            start,
            end
        );
        DrawActiveSegment(
            drawingContext,
            content,
            start,
            end
        );
        DrawExcludedRanges(
            drawingContext,
            content,
            start,
            end
        );
        DrawWaveform(drawingContext, content, start, end);
        DrawSelectionEdges(
            drawingContext,
            content,
            start,
            end
        );
        DrawSplitPoints(
            drawingContext,
            content,
            start,
            end
        );
        DrawPlayhead(drawingContext, content, start, end);
        drawingContext.Pop();

        DrawNowLabel(drawingContext, content);
    }

    protected override void OnMouseLeftButtonDown(
        MouseButtonEventArgs args
    )
    {
        base.OnMouseLeftButtonDown(args);
        if (!TryGetTimeline(out var start, out var end))
        {
            return;
        }

        Focus();
        var point = args.GetPosition(this);
        var position = PositionFromX(point.X, start, end);

        if (args.ClickCount >= 2)
        {
            HandleDoubleClick(point, position, start, end);
            args.Handled = true;
            return;
        }

        if (
            SelectionStart is { } selectionStart
            && Math.Abs(
                XFromPosition(selectionStart, start, end) - point.X
            ) <= HandleHitWidth
        )
        {
            CaptureMouse();
            _dragMode = DragMode.Start;
            RaiseActiveEdge(SelectionEdge.Start);
            return;
        }

        if (
            SelectionEnd is { } selectionEnd
            && Math.Abs(
                XFromPosition(selectionEnd, start, end) - point.X
            ) <= HandleHitWidth
        )
        {
            CaptureMouse();
            _dragMode = DragMode.End;
            RaiseActiveEdge(SelectionEdge.End);
            return;
        }

        var splitIndex = FindSplitHit(point.X, start, end);
        if (splitIndex >= 0)
        {
            CaptureMouse();
            _dragMode = DragMode.Split;
            _dragSplitIndex = splitIndex;
            RaiseSegmentActivated(splitIndex);
            return;
        }

        CaptureMouse();
        _dragAnchor = position;
        _mouseDownPoint = point;
        if (
            SelectionStart is { } existingStart
            && SelectionEnd is { } existingEnd
            && position >= existingStart
            && position <= existingEnd
        )
        {
            _dragMode = DragMode.PendingNew;
            return;
        }

        BeginNewSelection(position);
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);
        if (
            _dragMode == DragMode.None
            || args.LeftButton != MouseButtonState.Pressed
            || !TryGetTimeline(out var start, out var end)
        )
        {
            return;
        }

        var position = PositionFromX(
            args.GetPosition(this).X,
            start,
            end
        );
        switch (_dragMode)
        {
            case DragMode.PendingNew:
                var point = args.GetPosition(this);
                if (
                    Math.Abs(point.X - _mouseDownPoint.X)
                        < SystemParameters.MinimumHorizontalDragDistance
                    && Math.Abs(point.Y - _mouseDownPoint.Y)
                        < SystemParameters.MinimumVerticalDragDistance
                )
                {
                    return;
                }

                BeginNewSelection(_dragAnchor);
                SelectionStart = Min(_dragAnchor, position);
                SelectionEnd = Max(_dragAnchor, position);
                break;
            case DragMode.New:
                SelectionStart = Min(_dragAnchor, position);
                SelectionEnd = Max(_dragAnchor, position);
                RaiseActiveEdge(
                    position >= _dragAnchor
                        ? SelectionEdge.End
                        : SelectionEdge.Start
                );
                break;
            case DragMode.Start when SelectionEnd is { } selectionEnd:
                SelectionStart = Min(position, selectionEnd);
                break;
            case DragMode.End when SelectionStart is { } selectionStart:
                SelectionEnd = Max(position, selectionStart);
                break;
            case DragMode.Split:
                MoveSplit(position);
                return;
        }

        RaiseSelectionChanged();
    }

    protected override void OnMouseLeftButtonUp(
        MouseButtonEventArgs args
    )
    {
        base.OnMouseLeftButtonUp(args);
        if (_dragMode == DragMode.None)
        {
            return;
        }

        var completedMode = _dragMode;
        var position = TryGetTimeline(out var timelineStart, out var timelineEnd)
            ? PositionFromX(
                args.GetPosition(this).X,
                timelineStart,
                timelineEnd
            )
            : _dragAnchor;
        ReleaseMouseCapture();
        _dragMode = DragMode.None;
        _dragSplitIndex = -1;
        if (completedMode == DragMode.PendingNew)
        {
            RaiseSegmentActivated(FindSegmentIndex(position));
            return;
        }

        if (completedMode == DragMode.Split)
        {
            return;
        }

        if (
            SelectionStart is not { } start
            || SelectionEnd is not { } end
            || end <= start
        )
        {
            SelectionStart = null;
            SelectionEnd = null;
        }

        RaiseSelectionChanged();
    }

    protected override void OnLostMouseCapture(
        MouseEventArgs args
    )
    {
        base.OnLostMouseCapture(args);
        _dragMode = DragMode.None;
        _dragSplitIndex = -1;
    }

    private void HandleDoubleClick(
        Point point,
        TimeSpan position,
        TimeSpan timelineStart,
        TimeSpan timelineEnd
    )
    {
        if (
            SelectionStart is not { } selectionStart
            || SelectionEnd is not { } selectionEnd
            || position <= selectionStart
            || position >= selectionEnd
        )
        {
            return;
        }

        if (
            Math.Abs(
                XFromPosition(
                    selectionStart,
                    timelineStart,
                    timelineEnd
                ) - point.X
            ) <= HandleHitWidth
            || Math.Abs(
                XFromPosition(
                    selectionEnd,
                    timelineStart,
                    timelineEnd
                ) - point.X
            ) <= HandleHitWidth
        )
        {
            return;
        }

        var splitIndex = FindSplitHit(
            point.X,
            timelineStart,
            timelineEnd
        );
        if (splitIndex >= 0)
        {
            SplitRequested?.Invoke(
                this,
                new WaveformSplitRequestedEventArgs(
                    splitIndex,
                    _splitPoints[splitIndex]
                )
            );
            return;
        }

        if (!CanAddSplit(position))
        {
            return;
        }

        SplitRequested?.Invoke(
            this,
            new WaveformSplitRequestedEventArgs(-1, position)
        );
    }

    private void BeginNewSelection(TimeSpan position)
    {
        NewSelectionStarted?.Invoke(this, EventArgs.Empty);
        _splitPoints.Clear();
        _activeSegmentIndex = 0;
        _dragMode = DragMode.New;
        SelectionStart = position;
        SelectionEnd = position;
        InvalidateVisual();
    }

    private void MoveSplit(TimeSpan position)
    {
        if (
            _dragSplitIndex < 0
            || _dragSplitIndex >= _splitPoints.Count
            || SelectionStart is not { } selectionStart
            || SelectionEnd is not { } selectionEnd
        )
        {
            return;
        }

        var minimum =
            (_dragSplitIndex == 0
                ? selectionStart
                : _splitPoints[_dragSplitIndex - 1])
            + MinimumSegmentDuration;
        var maximum =
            (_dragSplitIndex == _splitPoints.Count - 1
                ? selectionEnd
                : _splitPoints[_dragSplitIndex + 1])
            - MinimumSegmentDuration;
        var clamped = position < minimum
            ? minimum
            : position > maximum
                ? maximum
                : position;
        if (_splitPoints[_dragSplitIndex] == clamped)
        {
            return;
        }

        _splitPoints[_dragSplitIndex] = clamped;
        InvalidateVisual();
        SplitChanged?.Invoke(
            this,
            new WaveformSplitChangedEventArgs(
                _dragSplitIndex,
                clamped
            )
        );
    }

    private bool CanAddSplit(TimeSpan position)
    {
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
        return position - segmentStart >= MinimumSegmentDuration
            && segmentEnd - position >= MinimumSegmentDuration;
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

    private int FindSplitHit(
        double x,
        TimeSpan timelineStart,
        TimeSpan timelineEnd
    )
    {
        for (var index = 0; index < _splitPoints.Count; index++)
        {
            if (
                Math.Abs(
                    XFromPosition(
                        _splitPoints[index],
                        timelineStart,
                        timelineEnd
                    ) - x
                ) <= SplitHitWidth
            )
            {
                return index;
            }
        }

        return -1;
    }

    private void DrawSelectionBackground(
        DrawingContext drawingContext,
        Rect content,
        TimeSpan timelineStart,
        TimeSpan timelineEnd
    )
    {
        if (
            SelectionStart is not { } start
            || SelectionEnd is not { } end
        )
        {
            return;
        }

        var left = XFromPosition(start, timelineStart, timelineEnd);
        var right = XFromPosition(end, timelineStart, timelineEnd);
        drawingContext.DrawRectangle(
            SelectionBrush,
            null,
            new Rect(
                left,
                content.Top,
                Math.Max(0, right - left),
                content.Height
            )
        );
    }

    private void DrawActiveSegment(
        DrawingContext drawingContext,
        Rect content,
        TimeSpan timelineStart,
        TimeSpan timelineEnd
    )
    {
        if (
            _splitPoints.Count == 0
            || SelectionStart is not { } selectionStart
            || SelectionEnd is not { } selectionEnd
        )
        {
            return;
        }

        var segmentIndex = Math.Clamp(
            ActiveSegmentIndex,
            0,
            _splitPoints.Count
        );
        var segmentStart = segmentIndex == 0
            ? selectionStart
            : _splitPoints[segmentIndex - 1];
        var segmentEnd = segmentIndex == _splitPoints.Count
            ? selectionEnd
            : _splitPoints[segmentIndex];
        var left = XFromPosition(
            segmentStart,
            timelineStart,
            timelineEnd
        );
        var right = XFromPosition(
            segmentEnd,
            timelineStart,
            timelineEnd
        );
        drawingContext.DrawRectangle(
            ActiveSegmentBrush,
            null,
            new Rect(
                left,
                content.Top,
                Math.Max(0, right - left),
                content.Height
            )
        );
    }

    private void DrawSplitPoints(
        DrawingContext drawingContext,
        Rect content,
        TimeSpan timelineStart,
        TimeSpan timelineEnd
    )
    {
        for (var index = 0; index < _splitPoints.Count; index++)
        {
            var x = XFromPosition(
                _splitPoints[index],
                timelineStart,
                timelineEnd
            );
            drawingContext.DrawLine(
                SplitPen,
                new Point(x, content.Top),
                new Point(x, content.Bottom)
            );
            drawingContext.DrawEllipse(
                SplitPen.Brush,
                null,
                new Point(x, content.Top + 12),
                5,
                5
            );
        }

        if (
            _splitPoints.Count == 0
            || SelectionStart is not { } labelStart
            || SelectionEnd is not { } labelEnd
        )
        {
            return;
        }

        var boundaries = new List<TimeSpan>
        {
            labelStart,
        };
        boundaries.AddRange(_splitPoints);
        boundaries.Add(labelEnd);
        for (var index = 0; index < boundaries.Count - 1; index++)
        {
            var left = XFromPosition(
                boundaries[index],
                timelineStart,
                timelineEnd
            );
            var right = XFromPosition(
                boundaries[index + 1],
                timelineStart,
                timelineEnd
            );
            var text = CreateText(
                (index + 1).ToString(CultureInfo.CurrentCulture),
                10,
                index == ActiveSegmentIndex
                    ? Color.FromRgb(91, 231, 177)
                    : Color.FromRgb(201, 209, 220)
            );
            drawingContext.DrawText(
                text,
                new Point(
                    left + Math.Max(3, (right - left - text.Width) / 2),
                    content.Bottom - text.Height - 3
                )
            );
        }
    }

    private void DrawExcludedRanges(
        DrawingContext drawingContext,
        Rect content,
        TimeSpan timelineStart,
        TimeSpan timelineEnd
    )
    {
        if (Snapshot is null)
        {
            return;
        }

        foreach (var range in Snapshot.ExcludedRanges)
        {
            var left = XFromPosition(
                range.Start,
                timelineStart,
                timelineEnd
            );
            var right = XFromPosition(
                range.End,
                timelineStart,
                timelineEnd
            );
            if (right - left < 0.75)
            {
                // A subpixel gap cannot be represented usefully. Avoid
                // retaining a rectangle and a full set of hatch lines for
                // timing noise that is smaller than one screen pixel.
                continue;
            }

            var rangeRect = new Rect(
                left,
                content.Top,
                right - left,
                content.Height
            );
            drawingContext.DrawRectangle(
                ExcludedBrush,
                null,
                rangeRect
            );
            for (
                var x = rangeRect.Left - rangeRect.Height;
                x < rangeRect.Right;
                x += 10
            )
            {
                drawingContext.DrawLine(
                    HatchPen,
                    new Point(x, rangeRect.Bottom),
                    new Point(x + rangeRect.Height, rangeRect.Top)
                );
            }
        }
    }

    private void DrawWaveform(
        DrawingContext drawingContext,
        Rect content,
        TimeSpan timelineStart,
        TimeSpan timelineEnd
    )
    {
        if (
            Snapshot is null
            || Snapshot.Audio.IsEmpty
            || Snapshot.Format.BitsPerSample != 16
        )
        {
            return;
        }

        var center = content.Top + (content.Height / 2);
        var maximumHeight = content.Height * 0.43;
        var audioOffset = 0;
        foreach (var range in Snapshot.IncludedRanges)
        {
            var rangeByteCount = Math.Min(
                Snapshot.Format.GetAlignedByteCount(range.Duration),
                Snapshot.Audio.Length - audioOffset
            );
            if (rangeByteCount <= 0)
            {
                continue;
            }

            var left = (int)Math.Floor(
                XFromPosition(
                    range.Start,
                    timelineStart,
                    timelineEnd
                )
            );
            var right = (int)Math.Ceiling(
                XFromPosition(
                    range.End,
                    timelineStart,
                    timelineEnd
                )
            );
            var width = Math.Max(1, right - left);
            var frameCount =
                rangeByteCount / Snapshot.Format.BlockAlign;
            for (var x = left; x <= right; x++)
            {
                var localX = Math.Clamp(x - left, 0, width);
                var nextLocalX = Math.Clamp(
                    localX + 1,
                    0,
                    width
                );
                var firstFrame = (int)(
                    frameCount * (localX / (double)width)
                );
                var finalFrame = Math.Max(
                    firstFrame + 1,
                    (int)(
                        frameCount
                        * (nextLocalX / (double)width)
                    )
                );
                finalFrame = Math.Min(finalFrame, frameCount);
                var peak = FindPeak(
                    Snapshot.Audio.Span,
                    audioOffset,
                    firstFrame,
                    finalFrame,
                    Snapshot.Format
                );
                var height = Math.Max(
                    1,
                    maximumHeight * (peak / 32768d)
                );
                drawingContext.DrawLine(
                    WaveformPen,
                    new Point(x + 0.5, center - height),
                    new Point(x + 0.5, center + height)
                );
            }

            audioOffset += rangeByteCount;
        }
    }

    private static int FindPeak(
        ReadOnlySpan<byte> audio,
        int audioOffset,
        int firstFrame,
        int finalFrame,
        PcmAudioFormat format
    )
    {
        var frameCount = Math.Max(1, finalFrame - firstFrame);
        var step = Math.Max(1, frameCount / 128);
        var peak = 0;
        for (
            var frame = firstFrame;
            frame < finalFrame;
            frame += step
        )
        {
            var frameOffset =
                audioOffset + (frame * format.BlockAlign);
            for (
                var channel = 0;
                channel < format.Channels;
                channel++
            )
            {
                var sampleOffset =
                    frameOffset + (channel * sizeof(short));
                if (sampleOffset + sizeof(short) > audio.Length)
                {
                    return peak;
                }

                var sample = BitConverter.ToInt16(
                    audio.Slice(sampleOffset, sizeof(short))
                );
                peak = Math.Max(peak, Math.Abs((int)sample));
            }
        }

        return peak;
    }

    private void DrawSelectionEdges(
        DrawingContext drawingContext,
        Rect content,
        TimeSpan timelineStart,
        TimeSpan timelineEnd
    )
    {
        if (
            SelectionStart is not { } start
            || SelectionEnd is not { } end
        )
        {
            return;
        }

        foreach (var position in new[] { start, end })
        {
            var x = XFromPosition(
                position,
                timelineStart,
                timelineEnd
            );
            drawingContext.DrawLine(
                SelectionEdgePen,
                new Point(x, content.Top),
                new Point(x, content.Bottom)
            );
            drawingContext.DrawRoundedRectangle(
                SelectionEdgePen.Brush,
                null,
                new Rect(x - 3, content.Top + 4, 6, 22),
                3,
                3
            );
        }
    }

    private void DrawPlayhead(
        DrawingContext drawingContext,
        Rect content,
        TimeSpan timelineStart,
        TimeSpan timelineEnd
    )
    {
        if (Playhead is not { } playhead)
        {
            return;
        }

        var x = XFromPosition(
            playhead,
            timelineStart,
            timelineEnd
        );
        drawingContext.DrawLine(
            NowPen,
            new Point(x, content.Top),
            new Point(x, content.Bottom)
        );
    }

    private void DrawEmptyState(
        DrawingContext drawingContext,
        Rect bounds
    )
    {
        var text = CreateText(
            AppStrings.NoBufferedAudio,
            14,
            Color.FromRgb(145, 153, 170)
        );
        drawingContext.DrawText(
            text,
            new Point(
                Math.Max(12, (bounds.Width - text.Width) / 2),
                Math.Max(12, (bounds.Height - text.Height) / 2)
            )
        );
    }

    private void DrawNowLabel(
        DrawingContext drawingContext,
        Rect content
    )
    {
        var text = CreateText(
            AppStrings.Now,
            10,
            Color.FromRgb(201, 209, 220)
        );
        drawingContext.DrawText(
            text,
            new Point(content.Right - text.Width, 4)
        );
    }

    private FormattedText CreateText(
        string value,
        double size,
        Color color
    ) =>
        new(
            value,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            size,
            new SolidColorBrush(color),
            VisualTreeHelper.GetDpi(this).PixelsPerDip
        );

    private bool TryGetTimeline(
        out TimeSpan start,
        out TimeSpan end
    )
    {
        start =
            Snapshot?.AvailableStart
            ?? Snapshot?.RequestedStart
            ?? TimeSpan.Zero;
        end =
            Snapshot?.AvailableEnd
            ?? Snapshot?.RequestedEnd
            ?? TimeSpan.Zero;
        return Snapshot is not null && end > start;
    }

    private TimeSpan PositionFromX(
        double x,
        TimeSpan start,
        TimeSpan end
    )
    {
        var width = Math.Max(
            1,
            ActualWidth - (HorizontalPadding * 2)
        );
        var fraction = Math.Clamp(
            (x - HorizontalPadding) / width,
            0,
            1
        );
        return start
            + TimeSpan.FromTicks(
                (long)Math.Round((end - start).Ticks * fraction)
            );
    }

    private double XFromPosition(
        TimeSpan position,
        TimeSpan start,
        TimeSpan end
    )
    {
        var width = Math.Max(
            1,
            ActualWidth - (HorizontalPadding * 2)
        );
        var fraction = Math.Clamp(
            (position - start).Ticks
                / (double)(end - start).Ticks,
            0,
            1
        );
        return HorizontalPadding + (fraction * width);
    }

    private void RaiseSelectionChanged() =>
        SelectionChanged?.Invoke(
            this,
            new WaveformSelectionChangedEventArgs(
                SelectionStart,
                SelectionEnd
            )
        );

    private void RaiseActiveEdge(SelectionEdge edge) =>
        ActiveEdgeChanged?.Invoke(
            this,
            new SelectionEdgeChangedEventArgs(edge)
        );

    private void RaiseSegmentActivated(int segmentIndex)
    {
        _activeSegmentIndex = Math.Clamp(
            segmentIndex,
            0,
            _splitPoints.Count
        );
        InvalidateVisual();
        SegmentActivated?.Invoke(
            this,
            new WaveformSegmentActivatedEventArgs(
                _activeSegmentIndex
            )
        );
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) =>
        left <= right ? left : right;

    private static TimeSpan Max(TimeSpan left, TimeSpan right) =>
        left >= right ? left : right;

    private static T Freeze<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private enum DragMode
    {
        None,
        PendingNew,
        New,
        Start,
        End,
        Split,
    }
}
