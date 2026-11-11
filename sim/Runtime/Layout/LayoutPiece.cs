namespace Worktown.Sim.Layout
{
    /// <summary>
    /// Spec 062a: one placed piece of a shop layout. <see cref="Id"/>: 1 to <see cref="ShopLayout.MaxPieceId"/>, given once and never
    /// reused (its stations and place surfaces are known by it). <see cref="XMm"/>, <see cref="ZMm"/>: the footprint's centre in world
    /// millimetres. <see cref="Turns"/>: 0 to 3 quarter turns (0: the cook side towards -z, like the back line). <see cref="LengthMm"/>: a
    /// variable-length counter's length (0 otherwise). <see cref="Tier"/> (1) and <see cref="PaidCents"/> (0) are reserved for spec 062c.
    /// </summary>
    public sealed record LayoutPiece(int Id, string TypeId, int XMm, int ZMm, int Turns, int LengthMm, int Tier, long PaidCents);
}
