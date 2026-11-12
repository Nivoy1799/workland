using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Save;
using Worktown.Sim.Layout;

namespace Worktown.Game.Layout
{
    /// <summary>
    /// Spec 062a: today's kitchen as data (Resources/Layouts/default.json: written once by LayoutExporter from the builder's kitchen,
    /// hand-maintained since), the layout a new shop, the open kitchen and a version 2 save start with. <see cref="Backfill"/> adds a
    /// required piece a saved layout lacks.
    /// </summary>
    public static class DefaultLayout
    {
        public const string ResourcePath = "Layouts/default";

        /// <summary>A fresh copy of default.json's layout part (null when the resource is missing).</summary>
        public static ShopSave.LayoutPart Part()
        {
            TextAsset text = Resources.Load<TextAsset>(ResourcePath);
            if (text == null)
            {
                Debug.LogError("[Layout] the default layout (Resources/" + ResourcePath + ".json) is missing");
                return null;
            }
            return JsonUtility.FromJson<ShopSave.LayoutPart>(text.text);
        }

        /// <summary>The default layout (an empty one when the resource is missing).</summary>
        public static ShopLayout Load(PieceCatalog catalog = null) => LayoutCodec.ToLayout(Part(), catalog ?? PieceCatalog.CreateDefault());

        /// <summary>
        /// Spec 062a, pure: every <see cref="PieceDef.Required"/> type (and each type in <paramref name="flagged"/>, how later specs add
        /// their pieces) that <paramref name="saved"/> lacks is added at its pose in <paramref name="standard"/> with a new id, when its
        /// footprint is free there (no placed piece of the same band overlaps it), else it is stored (062b). A layout without homes gets the
        /// standard homes whose piece is placed with the same type. <paramref name="notes"/> says what was added.
        /// </summary>
        public static ShopLayout Backfill(ShopLayout saved, ShopLayout standard, PieceCatalog catalog, IEnumerable<string> flagged = null,
            List<string> notes = null)
        {
            if (saved == null) return standard;
            if (standard == null || catalog == null) return saved;
            var wanted = new List<string>();
            foreach (PieceDef row in catalog.Rows) if (row.Required) wanted.Add(row.TypeId);
            if (flagged != null) foreach (string t in flagged) if (!wanted.Contains(t)) wanted.Add(t);

            var pieces = new List<LayoutPiece>(saved.Pieces);
            var stored = new List<int>(saved.Stored);
            var records = new List<LayoutPiece>(saved.StoredPieces); // spec 062b: a stored piece keeps its record (type, length, pose)
            int next = saved.NextId;
            foreach (string type in wanted)
            {
                if (saved.CountOf(type) > 0 || !catalog.TryGet(type, out PieceDef def)) continue;
                LayoutPiece model = null;
                foreach (LayoutPiece p in standard.Pieces) if (p.TypeId == type) { model = p; break; }
                if (model == null) continue;
                if (next > ShopLayout.MaxPieceId)
                {
                    // 062a review: no id is left (ids are never reused); the piece is not added and the layout may then fall back.
                    notes?.Add("no piece id left for " + type);
                    continue;
                }
                LayoutPiece added = model with { Id = next++ };
                if (IsFree(added, def, pieces, catalog))
                {
                    pieces.Add(added);
                    notes?.Add("added " + type + " as piece " + added.Id);
                }
                else
                {
                    stored.Add(added.Id);
                    records.Add(added);
                    notes?.Add("stored " + type + " as piece " + added.Id + " (its place is taken)");
                }
            }

            var homes = new List<ItemHome>(saved.Homes);
            if (homes.Count == 0)
                foreach (ItemHome h in standard.Homes)
                {
                    LayoutPiece onStandard = standard.Piece(h.PieceId);
                    LayoutPiece onSaved = null;
                    foreach (LayoutPiece p in pieces) if (p.Id == h.PieceId) { onSaved = p; break; }
                    if (onStandard != null && onSaved != null && onSaved.TypeId == onStandard.TypeId) homes.Add(h);
                }
            return new ShopLayout(saved.Version, pieces, stored, homes, next, records);
        }

        private static bool IsFree(LayoutPiece piece, PieceDef def, List<LayoutPiece> placed, PieceCatalog catalog)
        {
            RectMm mine = def.Footprint(piece);
            foreach (LayoutPiece other in placed)
            {
                if (!catalog.TryGet(other.TypeId, out PieceDef otherDef) || LayoutRules.Band(otherDef.Mount) != LayoutRules.Band(def.Mount)) continue;
                if (mine.Intersects(otherDef.Footprint(other))) return false;
            }
            return true;
        }
    }
}
