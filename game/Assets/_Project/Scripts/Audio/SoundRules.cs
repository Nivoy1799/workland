using UnityEngine;
using Worktown.Game.Cooking;

namespace Worktown.Game.Audio
{
    /// <summary>
    /// Spec 027b: which sound an action makes (docs/feel.md 2: a tock that fits the material). The item's own material
    /// decides (a pan clanks wherever it lands); a box or anything unknown takes the surface's (a burner is metal, a
    /// worktop wood).
    /// </summary>
    public static class SoundRules
    {
        /// <summary>The material of cookware: pan, pot and knife metal; plate and bowl ceramic; board wood.</summary>
        public static SoundMaterial MaterialOf(VesselKind kind)
        {
            switch (kind)
            {
                case VesselKind.Pan:
                case VesselKind.Pot:
                    return SoundMaterial.Metal;
                case VesselKind.Plate:
                case VesselKind.Bowl:
                    return SoundMaterial.Ceramic;
                default:
                    return SoundMaterial.Wood;
            }
        }

        /// <summary>The material of a carried thing (cookware, a knife, food), or null for anything else (a box).</summary>
        public static SoundMaterial? MaterialOf(Component item)
        {
            if (item == null) return null;
            if (item.TryGetComponent(out Vessel vessel)) return MaterialOf(vessel.Kind);
            if (item.TryGetComponent(out Knife _)) return SoundMaterial.Metal;
            if (item.TryGetComponent(out Food _)) return SoundMaterial.Food;
            return null;
        }

        /// <summary>The place tock: the item's material, else the surface's (metal on a burner, wood elsewhere).</summary>
        public static SoundKey PlaceSound(SoundMaterial? item, bool onBurner)
        {
            SoundMaterial material = item ?? (onBurner ? SoundMaterial.Metal : SoundMaterial.Wood);
            switch (material)
            {
                case SoundMaterial.Metal: return SoundKey.PlaceMetal;
                case SoundMaterial.Ceramic: return SoundKey.PlaceCeramic;
                case SoundMaterial.Food: return SoundKey.PlaceFood;
                default: return SoundKey.PlaceWood;
            }
        }
    }
}
