namespace Worktown.Sim.Layout
{
    /// <summary>
    /// 062b review: one item home that a layout edit moves: <see cref="Old"/> becomes <see cref="New"/> (a stored worktop's knife gets a home
    /// on another worktop). The edit's inverse swaps the two back, and only where the layout still has <see cref="New"/>.
    /// </summary>
    public sealed record HomeChange(ItemHome Old, ItemHome New);
}
