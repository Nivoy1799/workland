using System;
using System.Collections.Generic;
using System.Linq;

namespace Worktown.Sim.Layout
{
    /// <summary>
    /// Spec 062a: a shop's furniture as data: the placed <see cref="Pieces"/> (ascending id), the <see cref="Stored"/> piece ids (reserved
    /// for spec 062b: bought or picked up, not placed), the cookware's and tools' <see cref="Homes"/> and the next free piece id
    /// (<see cref="NextId"/>, never reused). Immutable: the constructor copies its inputs and sorts the pieces by id. Two layouts are equal
    /// when all their parts are (<see cref="Equals(ShopLayout)"/>).
    /// </summary>
    public sealed class ShopLayout : IEquatable<ShopLayout>
    {
        /// <summary>The layout data's version (the save's and default.json's "version").</summary>
        public const int CurrentVersion = 1;
        /// <summary>Piece ids are 1 to 2^24 - 1 (a surface id is the piece id shifted by 6 bits, which fits an int).</summary>
        public const int MaxPieceId = (1 << 24) - 1;

        public ShopLayout(int version, IEnumerable<LayoutPiece> pieces, IEnumerable<int> stored, IEnumerable<ItemHome> homes, int nextId)
        {
            Version = version;
            Pieces = (pieces ?? Array.Empty<LayoutPiece>()).Where(p => p != null).OrderBy(p => p.Id).ToList().AsReadOnly();
            Stored = (stored ?? Array.Empty<int>()).ToList().AsReadOnly();
            Homes = (homes ?? Array.Empty<ItemHome>()).Where(h => h != null).ToList().AsReadOnly();
            int highest = 0;
            foreach (LayoutPiece p in Pieces) highest = Math.Max(highest, p.Id);
            foreach (int id in Stored) highest = Math.Max(highest, id);
            NextId = Math.Max(nextId, highest + 1);
        }

        public int Version { get; }
        public IReadOnlyList<LayoutPiece> Pieces { get; }
        public IReadOnlyList<int> Stored { get; }
        public IReadOnlyList<ItemHome> Homes { get; }
        /// <summary>The id the next new piece gets: at least one above every id in use.</summary>
        public int NextId { get; }

        /// <summary>The placed piece with id <paramref name="id"/>, or null.</summary>
        public LayoutPiece? Piece(int id)
        {
            foreach (LayoutPiece p in Pieces)
                if (p.Id == id) return p;
            return null;
        }

        /// <summary>How many placed pieces have type <paramref name="typeId"/>.</summary>
        public int CountOf(string typeId) => Pieces.Count(p => string.Equals(p.TypeId, typeId, StringComparison.Ordinal));

        /// <summary>
        /// Spec 062a: the number a piece shows when its type is placed more than once ("Stove 1", "Stove 2"): its rank among the pieces of its
        /// type by ascending id, from 1; 0 when it is the only one of its type (or not placed).
        /// </summary>
        public int NumberOf(int id)
        {
            LayoutPiece? piece = Piece(id);
            if (piece == null || CountOf(piece.TypeId) < 2) return 0;
            int rank = 0;
            foreach (LayoutPiece p in Pieces)
            {
                if (!string.Equals(p.TypeId, piece.TypeId, StringComparison.Ordinal)) continue;
                rank++;
                if (p.Id == id) return rank;
            }
            return 0;
        }

        /// <summary>The same layout with <paramref name="pieces"/> instead of its pieces (stored, homes and the next id kept).</summary>
        public ShopLayout WithPieces(IEnumerable<LayoutPiece> pieces) => new ShopLayout(Version, pieces, Stored, Homes, NextId);

        public bool Equals(ShopLayout? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Version == other.Version && NextId == other.NextId && Pieces.SequenceEqual(other.Pieces)
                && Stored.SequenceEqual(other.Stored) && Homes.SequenceEqual(other.Homes);
        }

        public override bool Equals(object? obj) => obj is ShopLayout other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Version * 397 ^ NextId;
                foreach (LayoutPiece p in Pieces) h = h * 31 + p.GetHashCode();
                return h;
            }
        }
    }
}
