namespace Worktown.Sim.Layout
{
    /// <summary>
    /// Spec 062a: where a piece of cookware or a tool lies at the start (and comes back when lost): spot <see cref="Spot"/> of place surface
    /// <see cref="LocalSurface"/> (0 to 63, the piece's own order) of piece <see cref="PieceId"/>. <see cref="ItemKind"/>: "board", "knife",
    /// "spatula", "pan", "pot", "plate", "bowl", "cloth".
    /// </summary>
    public sealed record ItemHome(string ItemKind, int PieceId, int LocalSurface, int Spot);
}
