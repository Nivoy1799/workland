using UnityEngine;
using Worktown.Game.Cooking;

namespace Worktown.Game.Audio
{
    /// <summary>
    /// Spec 027b: which sound an action makes (docs/feel.md 2: a tock that fits the material). The item's own material
    /// decides (a pan clanks wherever it lands); a box or anything unknown takes the surface's (a burner is metal, a
    /// worktop wood).
    /// Spec 027c: the cooking loops follow the heat (level and pitch by zone) and the cues play once per zone entry.
    /// </summary>
    public static class SoundRules
    {
        /// <summary>Spec 027c: a cue does not repeat on the same vessel within this many seconds (no spam on flicker).</summary>
        public const float CueCooldown = 4f;
        /// <summary>Spec 027c: the pitch of the loop while Overdone and Burnt.</summary>
        public const float OverdonePitch = 1.06f;
        public const float BurntPitch = 1.1f;

        /// <summary>
        /// Spec 027c: the sizzle or bubble level (0..1) for a vessel: 0 when it is not on a burner that is on, else growing
        /// as the food heats up (quiet while Raw, full from Good on), a little less once burnt (it dries out).
        /// </summary>
        public static float LoopLevel(bool onHeat, HeatZone zone)
        {
            if (!onHeat) return 0f;
            switch (zone)
            {
                case HeatZone.None: return 0.3f; // an empty hot pan still hisses a little
                case HeatZone.Raw: return 0.4f;
                case HeatZone.Cooking: return 0.75f;
                case HeatZone.Burnt: return 0.85f;
                default: return 1f;
            }
        }

        /// <summary>Spec 027c: the loop's pitch rises slightly in Overdone (and a bit more once burnt).</summary>
        public static float LoopPitch(HeatZone zone)
        {
            if (zone == HeatZone.Burnt) return BurntPitch;
            return zone == HeatZone.Overdone ? OverdonePitch : 1f;
        }

        /// <summary>
        /// Spec 027c: the cue for a change of a vessel's zone byte, or null: Perfect when it enters Perfect from an earlier
        /// zone, Warning when the warning bit comes on, Burnt when it enters Burnt. Staying in a zone, going back to an
        /// earlier one, or a vessel seen for the first time (a join) play nothing.
        /// </summary>
        public static SoundKey? CueFor(HeatZone previous, bool previousWarn, HeatZone zone, bool warn)
        {
            if (zone == HeatZone.Burnt && previous != HeatZone.Burnt && previous != HeatZone.None) return SoundKey.Burnt;
            if (warn && !previousWarn && zone < HeatZone.Overdone && previous != HeatZone.None) return SoundKey.Warning;
            if (zone == HeatZone.Perfect && previous != HeatZone.None && previous < HeatZone.Perfect) return SoundKey.Perfect;
            return null;
        }

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
