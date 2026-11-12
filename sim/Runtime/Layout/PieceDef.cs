namespace Worktown.Sim.Layout
{
    /// <summary>
    /// Spec 062a: a row of the piece catalog: one type of furniture. Sizes in millimetres: <see cref="WidthMm"/> along the piece's own x
    /// (for a counter of variable length the default length), <see cref="DepthMm"/> along its own z (cook side to back). A counter of variable
    /// length has <see cref="MinLengthMm"/> to <see cref="MaxLengthMm"/> (both 0 for a fixed size). <see cref="HasGuestSide"/>: guests stand
    /// on its far side (the front counter's street side). <see cref="Required"/>: a layout needs one. <see cref="Movable"/>: false for fixed
    /// pieces (the yard's bin). Spec 062b: <see cref="Tall"/>: a floor piece taller than a counter (the fridge, the dry shelf, 1.8 m); a cook
    /// cannot reach over it to a wall or overhead piece (rule 4).
    /// </summary>
    public sealed record PieceDef(string TypeId, int WidthMm, int DepthMm, PieceMount Mount, bool IsCounter, bool HasGuestSide,
        bool HasStation, bool Required, bool Movable, int MinLengthMm, int MaxLengthMm, bool Tall = false)
    {
        /// <summary>A counter whose length is set per piece (<see cref="LayoutPiece.LengthMm"/>).</summary>
        public bool HasLength => MaxLengthMm > 0;

        /// <summary>The piece's width along its own x: its length for a counter of variable length (clamped to the row), else the row's width.</summary>
        public int WidthOf(LayoutPiece piece)
        {
            if (!HasLength || piece == null) return WidthMm;
            int length = piece.LengthMm;
            return length < MinLengthMm ? MinLengthMm : length > MaxLengthMm ? MaxLengthMm : length;
        }

        /// <summary>
        /// The piece's footprint on the floor: <see cref="WidthOf"/> along its own x, <see cref="DepthMm"/> along its own z, turned by its
        /// quarter turns (an odd number swaps the two) around its centre.
        /// </summary>
        public RectMm Footprint(LayoutPiece piece)
        {
            int w = WidthOf(piece), d = DepthMm;
            bool swap = piece != null && (piece.Turns & 1) == 1;
            return RectMm.Around(piece?.XMm ?? 0, piece?.ZMm ?? 0, swap ? d : w, swap ? w : d);
        }
    }
}
