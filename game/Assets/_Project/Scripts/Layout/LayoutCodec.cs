using System.Collections.Generic;
using Worktown.Game.Save;
using Worktown.Sim.Layout;

namespace Worktown.Game.Layout
{
    /// <summary>
    /// Spec 062a, pure: a save's (or default.json's) layout part and the sim's <see cref="ShopLayout"/>. Reading clamps what is out of
    /// range and notes it: an id outside 1 to ShopLayout.MaxPieceId or used twice drops that piece; turns wrap to 0-3; a counter's length
    /// clamps to its catalog row, a fixed piece's is 0; x and z clamp to PosePacking's range; the tier is at least 1 and the paid cents at
    /// least 0; a home's local surface 0-63 and spot 0 or more, else it is dropped. A piece of an unknown type is kept (the save keeps
    /// it for a later build), but LayoutSpawner does not spawn it.
    /// </summary>
    public static class LayoutCodec
    {
        public static ShopLayout ToLayout(ShopSave.LayoutPart part, PieceCatalog catalog, List<string> notes = null)
        {
            if (part == null) return new ShopLayout(ShopLayout.CurrentVersion, null, null, null, 1);
            var pieces = new List<LayoutPiece>();
            var seen = new HashSet<int>();
            if (part.pieces != null)
                foreach (ShopSave.PiecePart p in part.pieces)
                {
                    if (p == null) continue;
                    if (p.id < 1 || p.id > ShopLayout.MaxPieceId || !seen.Add(p.id))
                    {
                        notes?.Add("piece " + p.id + " (" + p.type + ") dropped: bad or repeated id");
                        continue;
                    }
                    pieces.Add(Read(p, catalog, notes));
                }
            var stored = new List<int>();
            if (part.stored != null)
                foreach (int id in part.stored)
                {
                    if (id < 1 || id > ShopLayout.MaxPieceId || !seen.Add(id)) { notes?.Add("stored piece " + id + " dropped"); continue; }
                    stored.Add(id);
                }
            var homes = new List<ItemHome>();
            if (part.homes != null)
                foreach (ShopSave.HomePart h in part.homes)
                {
                    if (h == null || string.IsNullOrEmpty(h.item) || h.local < 0 || h.local > SurfaceId.MaxLocal || h.spot < 0 || h.piece < 1)
                    {
                        notes?.Add("a home dropped: " + (h != null ? h.item + " on " + h.piece + "/" + h.local + "/" + h.spot : "null"));
                        continue;
                    }
                    homes.Add(new ItemHome(h.item, h.piece, h.local, h.spot));
                }
            return new ShopLayout(part.version, pieces, stored, homes, part.nextId);
        }

        private static LayoutPiece Read(ShopSave.PiecePart p, PieceCatalog catalog, List<string> notes)
        {
            string type = p.type ?? string.Empty;
            int x = Clamp("x", p.id, p.xMm, PosePacking.MinCoordinate, PosePacking.MaxCoordinate, notes);
            int z = Clamp("z", p.id, p.zMm, PosePacking.MinCoordinate, PosePacking.MaxCoordinate, notes);
            int turns = ((p.turns % 4) + 4) % 4;
            if (turns != p.turns) notes?.Add("piece " + p.id + " turns " + p.turns + " -> " + turns);
            int length = p.lengthMm;
            if (catalog != null && catalog.TryGet(type, out PieceDef def))
                length = def.HasLength ? Clamp("length", p.id, p.lengthMm == 0 ? def.WidthMm : p.lengthMm, def.MinLengthMm, def.MaxLengthMm, notes)
                    : Clamp("length", p.id, p.lengthMm, 0, 0, notes);
            else length = Clamp("length", p.id, p.lengthMm, 0, PosePacking.MaxLength, notes); // unknown type: kept as it was, in range
            int tier = p.tier < 1 ? 1 : p.tier;
            long paid = p.paidCents < 0 ? 0 : p.paidCents;
            return new LayoutPiece(p.id, type, x, z, turns, length, tier, paid);
        }

        public static ShopSave.LayoutPart ToPart(ShopLayout layout)
        {
            var part = new ShopSave.LayoutPart();
            if (layout == null) return part;
            part.version = layout.Version;
            part.nextId = layout.NextId;
            var pieces = new List<ShopSave.PiecePart>();
            foreach (LayoutPiece p in layout.Pieces)
                pieces.Add(new ShopSave.PiecePart
                {
                    id = p.Id, type = p.TypeId, xMm = p.XMm, zMm = p.ZMm, turns = p.Turns, lengthMm = p.LengthMm, tier = p.Tier, paidCents = p.PaidCents,
                });
            part.pieces = pieces.ToArray();
            var stored = new List<int>(layout.Stored);
            part.stored = stored.ToArray();
            var homes = new List<ShopSave.HomePart>();
            foreach (ItemHome h in layout.Homes) homes.Add(new ShopSave.HomePart { item = h.ItemKind, piece = h.PieceId, local = h.LocalSurface, spot = h.Spot });
            part.homes = homes.ToArray();
            return part;
        }

        private static int Clamp(string what, int id, int value, int min, int max, List<string> notes)
        {
            int clamped = value < min ? min : value > max ? max : value;
            if (clamped != value) notes?.Add("piece " + id + " " + what + " " + value + " -> " + clamped);
            return clamped;
        }
    }
}
