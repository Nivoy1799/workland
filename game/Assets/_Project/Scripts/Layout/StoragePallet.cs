using UnityEngine;

namespace Worktown.Game.Layout
{
    /// <summary>
    /// Spec 062b: the storage pallet in the yard by the back door (a fixed piece): where stored pieces wait. In build mode a right click on
    /// it stores the held piece, a left click takes a stored one out (it sits on the pallet, held, until it is placed), the wheel picks
    /// which one comes out next. Holds no rules; its sign reads <see cref="SignText"/>.
    /// </summary>
    public sealed class StoragePallet : MonoBehaviour
    {
        public const string SignText = "Storage";

        /// <summary>Whether <paramref name="piece"/> is the pallet (or a part of it).</summary>
        public static bool Is(PieceBehaviour piece) => piece != null && piece.TypeId == Worktown.Sim.Layout.PieceCatalog.StoragePallet;

        private static readonly Worktown.Sim.Layout.PieceCatalog Catalog = Worktown.Sim.Layout.PieceCatalog.CreateDefault();

        /// <summary>
        /// 062b review: whether <paramref name="piece"/>'s centre lies on a registered pallet (a stored piece taken out waits there; a placed
        /// piece never can: the pallet's footprint is in the floor band).
        /// </summary>
        public static bool Covers(PieceBehaviour piece)
        {
            if (piece == null || Is(piece) || piece.PieceId <= 0) return false;
            foreach (PieceBehaviour p in LayoutRegistry.Pieces)
            {
                if (!Is(p) || !Catalog.TryGet(p.TypeId, out Worktown.Sim.Layout.PieceDef def)) continue;
                var at = new Worktown.Sim.Layout.LayoutPiece(p.PieceId, p.TypeId, p.XMm, p.ZMm, p.Turns, p.LengthMm, 1, 0);
                if (def.Footprint(at).Contains(piece.XMm, piece.ZMm)) return true;
            }
            return false;
        }
    }
}
