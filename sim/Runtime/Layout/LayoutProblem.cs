namespace Worktown.Sim.Layout
{
    /// <summary>
    /// Spec 062a: one broken layout rule. <see cref="PieceId"/>: the piece it is about (0: the layout as a whole); <see cref="OtherId"/>:
    /// the piece in the way (0: none or a wall); <see cref="XMm"/>, <see cref="ZMm"/>: where to show it (the piece's centre, or the middle
    /// of the overlap); <see cref="Text"/>: a short line for spec 062b's ghost ("Too close to the fryer: aisles need 0.9 m").
    /// </summary>
    public sealed record LayoutProblem(LayoutProblemKind Kind, int PieceId, int OtherId, int XMm, int ZMm, string Text);
}
