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
    }
}
