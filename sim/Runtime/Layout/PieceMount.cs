namespace Worktown.Sim.Layout
{
    /// <summary>
    /// Spec 062a: where a piece of furniture stands. <see cref="InLine"/>: a module of a counter line (the back line, the front counter);
    /// <see cref="Floor"/>: on the floor on its own; <see cref="Overhead"/>: hanging above the counters; <see cref="Wall"/>: on a wall;
    /// <see cref="Yard"/>: outside the room, fixed.
    /// </summary>
    public enum PieceMount
    {
        InLine = 0,
        Floor = 1,
        Overhead = 2,
        Wall = 3,
        Yard = 4,
    }
}
