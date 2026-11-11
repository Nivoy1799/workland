namespace Worktown.Game.Layout
{
    /// <summary>
    /// Spec 062a, pure: a place surface's stable id, its piece's id and its local number on that piece:
    /// <c>pieceId &lt;&lt; 6 | local</c>. Local numbers are 0 to <see cref="MaxLocal"/> in the prefab's order (the order the builder made
    /// them in); piece ids are 1 to <see cref="MaxPiece"/> (Worktown.Sim.Layout.ShopLayout.MaxPieceId), so every id fits an int and is
    /// never negative. <see cref="None"/> (-1) is no surface. The wire (RequestPlace) and the server's spot table carry these ids.
    /// </summary>
    public static class SurfaceId
    {
        public const int None = -1;
        public const int LocalBits = 6;
        public const int MaxLocal = (1 << LocalBits) - 1;
        public const int MaxPiece = (1 << 24) - 1;

        /// <summary>The id of surface <paramref name="local"/> of piece <paramref name="pieceId"/>, or <see cref="None"/> when either is out of range.</summary>
        public static int Of(int pieceId, int local)
        {
            if (pieceId < 1 || pieceId > MaxPiece || local < 0 || local > MaxLocal) return None;
            return (pieceId << LocalBits) | local;
        }

        /// <summary>The piece and local number of <paramref name="id"/>; false for <see cref="None"/> or any id no piece can have.</summary>
        public static bool TryUnpack(int id, out int pieceId, out int local)
        {
            pieceId = id >> LocalBits;
            local = id & MaxLocal;
            if (id < 0 || pieceId < 1 || pieceId > MaxPiece)
            {
                pieceId = 0;
                local = 0;
                return false;
            }
            return true;
        }

        /// <summary>The piece id of <paramref name="id"/>, or 0.</summary>
        public static int PieceOf(int id) => TryUnpack(id, out int piece, out _) ? piece : 0;

        /// <summary>The local number of <paramref name="id"/>, or -1.</summary>
        public static int LocalOf(int id) => TryUnpack(id, out _, out int local) ? local : -1;
    }
}
