namespace Hookline.App;

public sealed class WaveformSelectionChangedEventArgs(
    TimeSpan? start,
    TimeSpan? end
) : EventArgs
{
    public TimeSpan? Start { get; } = start;

    public TimeSpan? End { get; } = end;
}

public sealed class SelectionEdgeChangedEventArgs(
    SelectionEdge edge
) : EventArgs
{
    public SelectionEdge Edge { get; } = edge;
}

public sealed class WaveformSplitChangedEventArgs(
    int splitIndex,
    TimeSpan position
) : EventArgs
{
    public int SplitIndex { get; } = splitIndex;

    public TimeSpan Position { get; } = position;
}

public sealed class WaveformSplitRequestedEventArgs(
    int splitIndex,
    TimeSpan position
) : EventArgs
{
    public int SplitIndex { get; } = splitIndex;

    public TimeSpan Position { get; } = position;
}

public sealed class WaveformSegmentActivatedEventArgs(
    int segmentIndex
) : EventArgs
{
    public int SegmentIndex { get; } = segmentIndex;
}
