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
