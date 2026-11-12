using System.Collections.Generic;
using System.Linq;

namespace Worktown.Sim.Layout
{
    /// <summary>
    /// Spec 062b, pure: which counter-line pieces join into one run. InLine pieces of a known type with the same quarter turns whose back
    /// edges (the side away from the cook: <see cref="BackEdge"/>) lie on the same line and whose ends touch (a gap of at most
    /// <see cref="JoinGapMm"/>: the default kitchen keeps 20 to 40 mm between its modules) form one run. Every InLine piece is in exactly one
    /// run (a piece alone is a run of one). Deterministic: runs ordered by turns, back edge and their first piece's start; the pieces of a
    /// run ordered along the line.
    /// </summary>
    public static class CounterRuns
    {
        /// <summary>The widest gap between two pieces of one run (the default kitchen: 20 to 40 mm between modules).</summary>
        public const int JoinGapMm = 50;

        public static IReadOnlyList<IReadOnlyList<LayoutPiece>> Of(ShopLayout layout, PieceCatalog catalog)
        {
            var runs = new List<IReadOnlyList<LayoutPiece>>();
            if (layout == null || catalog == null) return runs.AsReadOnly();
            var lines = new List<(LayoutPiece Piece, int Turns, int Back, int Min, int Max)>();
            foreach (LayoutPiece p in layout.Pieces)
            {
                if (!catalog.TryGet(p.TypeId, out PieceDef def) || def.Mount != PieceMount.InLine) continue;
                RectMm r = def.Footprint(p);
                int t = Wrap(p.Turns);
                (int min, int max) = Along(r, t);
                lines.Add((p, t, BackEdge(r, t), min, max));
            }
            foreach (var group in lines.GroupBy(l => (l.Turns, l.Back)).OrderBy(g => g.Key.Turns).ThenBy(g => g.Key.Back))
            {
                var current = new List<LayoutPiece>();
                int end = int.MinValue;
                foreach (var l in group.OrderBy(l => l.Min).ThenBy(l => l.Piece.Id))
                {
                    if (current.Count > 0 && l.Min > end + JoinGapMm)
                    {
                        runs.Add(current.AsReadOnly());
                        current = new List<LayoutPiece>();
                    }
                    end = current.Count == 0 ? l.Max : System.Math.Max(end, l.Max);
                    current.Add(l.Piece);
                }
                if (current.Count > 0) runs.Add(current.AsReadOnly());
            }
            return runs.AsReadOnly();
        }

        /// <summary>
        /// The coordinate of a footprint's back edge, the side away from the cook after <paramref name="turns"/> quarter turns: 0 its max z,
        /// 1 its max x, 2 its min z, 3 its min x (LayoutRules.CookDirection points the other way).
        /// </summary>
        public static int BackEdge(RectMm rect, int turns)
        {
            switch (Wrap(turns))
            {
                case 1: return rect.MaxX;
                case 2: return rect.MinZ;
                case 3: return rect.MinX;
                default: return rect.MaxZ;
            }
        }

        /// <summary>A footprint's extent along its line: x for 0 or 2 quarter turns, z for 1 or 3.</summary>
        public static (int Min, int Max) Along(RectMm rect, int turns) => AlongX(turns) ? (rect.MinX, rect.MaxX) : (rect.MinZ, rect.MaxZ);

        /// <summary>True when a piece with <paramref name="turns"/> quarter turns runs along x (0 or 2).</summary>
        public static bool AlongX(int turns) => (Wrap(turns) & 1) == 0;

        private static int Wrap(int turns) => ((turns % 4) + 4) % 4;
    }
}
