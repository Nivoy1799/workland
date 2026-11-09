using UnityEngine;
using Worktown.Game.Cooking;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 035, pure: the cleaning stations' sizes and the numbers their views draw with, shared by the catalog's graybox models, the
    /// scene builder and the views (docs/art-direction.md rows "Sink", "Dish return", "Kitchen trash can", "Bin (outside)", "Trash bag",
    /// "Wipe cloth"). Station-local metres, the base on the floor (or the counter top) at the origin, the front (the cook's side) on -Z
    /// as the station is placed (the catalog builds fronts on +Z; the scene turns them).
    /// </summary>
    public static class CleaningArtRules
    {
        // ---- the sink (a back-line station: the worktop's footprint and height) ----

        public const float SinkWidth = 1.2f;
        public const float SinkDepth = 1.2f;
        public static float SinkHeight => ArtSizeTable.WorktopHeight;
        /// <summary>The basin on the left half (towards the plates), its size and depth below the top.</summary>
        public static readonly Vector3 BasinCentre = new Vector3(-0.27f, 0f, -0.05f);
        public static readonly Vector2 BasinSize = new Vector2(0.52f, 0.42f);
        public const float BasinDepth = 0.2f;
        /// <summary>The water stands this far below the top.</summary>
        public const float WaterBelowTop = 0.07f;
        /// <summary>The drainboard (a place surface) on the right half: its centre and size on the top.</summary>
        public static readonly Vector2 DrainCentre = new Vector2(0.3f, -0.1f);
        public static readonly Vector2 DrainSize = new Vector2(0.5f, 0.6f);
        /// <summary>Bubbles drawn on the water: at most this many (more with more dishes, more while scrubbing).</summary>
        public const int MaxBubbles = 8;

        // ---- the dish return (on the front counter) ----

        public const float ReturnWidth = 0.6f;
        public const float ReturnDepth = 0.4f;
        public const float ReturnHeight = 0.3f;
        /// <summary>The dirty plates stand on the rack's lower board, this high above the counter top.</summary>
        public const float ReturnShelfHeight = 0.04f;
        /// <summary>At most this many plates are drawn in the return's stack (the count says the rest).</summary>
        public const int ReturnStackMax = 6;

        // ---- the trash can (by the staff gap) and the bin (outside) ----

        public const float TrashWidth = 0.46f;
        public const float TrashDepth = 0.46f;
        public const float TrashHeight = 0.85f;
        /// <summary>The trash inside rises from this height to just below the rim as it fills.</summary>
        public const float TrashFloor = 0.1f;
        public const float TrashRimGap = 0.06f;
        /// <summary>The lid swings open this far (degrees) at a discard and falls shut after <see cref="LidOpenSeconds"/>.</summary>
        public const float LidOpenAngle = 70f;
        /// <summary>The kitchen can's lid stands open at rest (the fill reads from the aisle); a discard swings it down and back up.</summary>
        public const float LidRestOpen = 80f;
        public const float LidOpenSeconds = 0.35f;

        public const float BinWidth = 0.62f;
        public const float BinDepth = 0.74f;
        public const float BinHeight = 1.1f;

        // ---- the carried things ----

        /// <summary>A full trash bag (world size: carried with both hands), its largest side.</summary>
        public const float BagSize = 0.45f;
        /// <summary>The wipe cloth (a tool: real 0.25 m x1.15), folded flat.</summary>
        public const float ClothReal = 0.25f;
        public static float ClothSize => ClothReal * 1.15f;
        public const float ClothThickness = 0.02f;

        // ---- the looks ----

        /// <summary>How high the trash is drawn (local y of its top) at <paramref name="fillShare"/> 0..1.</summary>
        public static float TrashLevel(float fillShare) =>
            TrashFloor + Mathf.Clamp01(fillShare) * (TrashHeight - TrashRimGap - TrashFloor);

        /// <summary>The bag's drawn scale for a bag of <paramref name="fillShare"/> 0..1 (a small bag still reads as a bag).</summary>
        public static float BagScale(float fillShare) => 0.55f + 0.45f * Mathf.Clamp01(fillShare);

        /// <summary>The lid's angle <paramref name="since"/> seconds after a discard (open, then falling shut with a little bounce).</summary>
        public static float LidAngle(float since)
        {
            if (since < 0f || since > LidOpenSeconds + 0.2f) return 0f;
            if (since <= LidOpenSeconds * 0.3f) return LidOpenAngle * since / (LidOpenSeconds * 0.3f);
            if (since <= LidOpenSeconds) return LidOpenAngle * (1f - (since - LidOpenSeconds * 0.3f) / (LidOpenSeconds * 0.7f));
            return 6f * Mathf.Sin((since - LidOpenSeconds) / 0.2f * Mathf.PI); // the bounce after the thump
        }

        /// <summary>The cloth's point on the spill <paramref name="seconds"/> into a wipe: small circles (radius m) about the puddle's centre.</summary>
        public static Vector2 WipeCircle(double seconds, float radius = 0.06f)
        {
            double a = seconds * 2.0 * System.Math.PI * 2.2; // a little over two turns a second
            return new Vector2((float)System.Math.Cos(a) * radius, (float)System.Math.Sin(a) * radius * 0.7f);
        }

        /// <summary>The puddle's drawn size (share of its full size) <paramref name="share"/> 0..1 through a wipe.</summary>
        public static float PuddleLeft(float share) => 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(share));

        /// <summary>The plate being scrubbed wobbles (degrees) and turns a little <paramref name="seconds"/> into the scrub.</summary>
        public static float ScrubWobble(double seconds) => 9f * (float)System.Math.Sin(seconds * 2.0 * System.Math.PI * 4.0);

        /// <summary>Bubble <paramref name="i"/>'s point on the water (basin-local x, z; deterministic, no allocations) at <paramref name="t"/> s.</summary>
        public static Vector2 BubblePoint(int i, float t, bool scrubbing)
        {
            float speed = scrubbing ? 1.8f : 0.35f;
            float a = i * 2.39996f + t * speed * (i % 2 == 0 ? 1f : -1f); // the golden angle spreads them
            float r = 0.05f + 0.035f * (i % 4);
            return new Vector2(Mathf.Cos(a) * r * BasinSize.x / 0.42f, Mathf.Sin(a) * r);
        }

        /// <summary>How many bubbles show with <paramref name="basin"/> dishes in the sink (none without; more while scrubbing).</summary>
        public static int BubbleCount(int basin, bool scrubbing)
        {
            if (basin <= 0 && !scrubbing) return 0;
            int n = 2 + 2 * basin + (scrubbing ? 3 : 0);
            return n > MaxBubbles ? MaxBubbles : n;
        }

        /// <summary>Spec 035: the station footprints for the fixtures (width, height, depth; the cleaning kinds only).</summary>
        public static Vector3 SizeOf(StationKind kind)
        {
            switch (kind)
            {
                case StationKind.Sink: return new Vector3(SinkWidth, SinkHeight, SinkDepth);
                case StationKind.DishReturn: return new Vector3(ReturnWidth, ReturnHeight, ReturnDepth);
                case StationKind.TrashCan: return new Vector3(TrashWidth, TrashHeight, TrashDepth);
                case StationKind.Bin: return new Vector3(BinWidth, BinHeight, BinDepth);
                default: return Vector3.zero;
            }
        }

        /// <summary>Where a cleaning station stands: the dish return on the front counter's top, the others on the floor.</summary>
        public static float BottomOf(StationKind kind) => kind == StationKind.DishReturn ? ArtSizeTable.ServingCounterHeight : 0f;
    }
}
