using System.Collections.Generic;
using System.Linq;

namespace Worktown.Sim.Layout
{
    /// <summary>
    /// Spec 062b: one build action as data, pure. <see cref="LayoutEditKind.Move"/> puts placed piece <see cref="PieceId"/> at
    /// (<see cref="XMm"/>, <see cref="ZMm"/>) with <see cref="Turns"/> (its centre, 0 to 3 quarter turns); <see cref="LayoutEditKind.Store"/>
    /// takes it out of the layout onto the storage pallet (the pose is ignored); <see cref="LayoutEditKind.TakeOut"/> places a stored piece
    /// at the pose. <see cref="TryApply"/> gives the new layout and the edit that undoes it. It checks only that the piece is where the edit
    /// needs it; the layout rules (LayoutRules.Validate) are the caller's job. Immutable.
    /// </summary>
    public sealed record LayoutEdit(LayoutEditKind Kind, int PieceId, int XMm, int ZMm, int Turns)
    {
        /// <summary>
        /// 062b review: the item homes the edit moves (old to new), or null. A stored worktop's tools get homes on another worktop (else they
        /// would not come back on the next load). Applying replaces each old home that the layout still has; the inverse carries only the
        /// changes that were made, swapped, so an undo restores these homes and leaves every other home (another player's) as it is.
        /// </summary>
        public IReadOnlyList<HomeChange>? HomeChanges { get; init; }

        public static LayoutEdit Move(int pieceId, int xMm, int zMm, int turns) => new LayoutEdit(LayoutEditKind.Move, pieceId, xMm, zMm, Wrap(turns));
        public static LayoutEdit Store(int pieceId) => new LayoutEdit(LayoutEditKind.Store, pieceId, 0, 0, 0);
        public static LayoutEdit TakeOut(int pieceId, int xMm, int zMm, int turns) => new LayoutEdit(LayoutEditKind.TakeOut, pieceId, xMm, zMm, Wrap(turns));

        /// <summary>
        /// The edit applied to <paramref name="layout"/>: <paramref name="result"/> is the new layout (the version, the homes and the next
        /// id kept) and <paramref name="inverse"/> the edit that brings <paramref name="layout"/> back (a move back, a take-out at the old
        /// pose, a store). False (both null) when the piece is not placed (Move, Store) or not stored with a record (TakeOut).
        /// </summary>
        public bool TryApply(ShopLayout layout, out ShopLayout result, out LayoutEdit inverse)
        {
            result = null!;
            inverse = null!;
            if (layout == null || !TryApplyPieces(layout, out result, out inverse)) return false;
            if (HomeChanges == null) return true;
            var homes = new List<ItemHome>(result.Homes);
            var made = new List<HomeChange>();
            foreach (HomeChange change in HomeChanges)
            {
                if (change == null) continue;
                int at = homes.IndexOf(change.Old);
                if (at < 0) continue; // that home changed since: left as it is
                homes[at] = change.New;
                made.Add(new HomeChange(change.New, change.Old));
            }
            result = result.WithHomes(homes);
            inverse = inverse with { HomeChanges = made.AsReadOnly() };
            return true;
        }

        private bool TryApplyPieces(ShopLayout layout, out ShopLayout result, out LayoutEdit inverse)
        {
            result = null!;
            inverse = null!;
            switch (Kind)
            {
                case LayoutEditKind.Move:
                {
                    LayoutPiece? piece = layout.Piece(PieceId);
                    if (piece == null) return false;
                    LayoutPiece moved = piece with { XMm = XMm, ZMm = ZMm, Turns = Wrap(Turns) };
                    result = layout.WithPieces(layout.Pieces.Select(p => p.Id == PieceId ? moved : p));
                    inverse = Move(PieceId, piece.XMm, piece.ZMm, piece.Turns);
                    return true;
                }
                case LayoutEditKind.Store:
                {
                    LayoutPiece? piece = layout.Piece(PieceId);
                    if (piece == null) return false;
                    var stored = new List<int>(layout.Stored) { PieceId };
                    var records = new List<LayoutPiece>(layout.StoredPieces) { piece };
                    result = layout.With(layout.Pieces.Where(p => p.Id != PieceId), stored, records);
                    inverse = TakeOut(PieceId, piece.XMm, piece.ZMm, piece.Turns);
                    return true;
                }
                case LayoutEditKind.TakeOut:
                {
                    LayoutPiece? record = layout.StoredPiece(PieceId);
                    if (record == null || layout.Piece(PieceId) != null) return false;
                    LayoutPiece placed = record with { XMm = XMm, ZMm = ZMm, Turns = Wrap(Turns) };
                    result = layout.With(layout.Pieces.Concat(new[] { placed }), layout.Stored.Where(id => id != PieceId),
                        layout.StoredPieces.Where(p => p.Id != PieceId));
                    inverse = Store(PieceId);
                    return true;
                }
                default:
                    return false;
            }
        }

        private static int Wrap(int turns) => ((turns % 4) + 4) % 4;
    }
}
