namespace Worktown.Sim.Layout
{
    /// <summary>Spec 062b: what a <see cref="LayoutEdit"/> does to a layout.</summary>
    public enum LayoutEditKind
    {
        /// <summary>A placed piece goes to a new pose (a move, a turn, or both).</summary>
        Move = 0,
        /// <summary>A placed piece leaves the layout and waits on the storage pallet (its record keeps its last pose).</summary>
        Store = 1,
        /// <summary>A stored piece is placed at a pose.</summary>
        TakeOut = 2,
    }
}
