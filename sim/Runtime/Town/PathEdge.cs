namespace Worktown.Sim.Town
{
    /// <summary>
    /// Spec 064: an undirected edge between nodes <see cref="A"/> and <see cref="B"/>. <see cref="WidthMm"/> 1500..20000.
    /// <see cref="FixedSeconds"/> is the ride time of a Lift or Funicular (&gt; 0) and 0 for walked edges.
    /// </summary>
    public sealed record PathEdge(int A, int B, PathEdgeKind Kind, int WidthMm, int FixedSeconds);
}
