namespace Worktown.Sim.Layout
{
    /// <summary>Spec 062a: what a <see cref="LayoutProblem"/> is about, in the order of <see cref="LayoutRules.Validate"/>'s rules.</summary>
    public enum LayoutProblemKind
    {
        /// <summary>Rule 1: a piece is not inside the floor (a yard piece: not outside it).</summary>
        Outside = 0,
        /// <summary>Rule 1: a wall piece does not touch a wall.</summary>
        OffWall = 1,
        /// <summary>Rule 2: two pieces of the same band overlap (OtherId 0: a piece goes into a wall).</summary>
        Overlap = 2,
        /// <summary>Rule 2: a piece covers a keep-out rect (a door).</summary>
        KeepOut = 3,
        /// <summary>Rule 3: a piece's aisle is not free (OtherId: the piece in it, 0 a wall).</summary>
        Aisle = 4,
        /// <summary>Rule 4: no cook can reach a station's access side.</summary>
        Unreachable = 5,
        /// <summary>Rule 5: guests cannot reach any serving counter from the street.</summary>
        NoGuestCounter = 6,
        /// <summary>Rule 6: a counter with a guest side does not have the guests on one side and the kitchen on the other.</summary>
        NoGuestSide = 7,
        /// <summary>Rule 7: a required type is not placed.</summary>
        Missing = 8,
    }
}
