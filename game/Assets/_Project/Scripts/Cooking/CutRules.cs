namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 019: cutting by hand on the board. One stroke halves whole food, a second gives slices; turn the
    /// food 90 degrees and cut again for sticks, turn and cut again for dice. Turning whole or halved food
    /// changes nothing. The shapes are the sim cut op's shape choices. Pure, server side.
    /// </summary>
    public static class CutRules
    {
        /// <summary>Spec 019 open question 5: a board's knife counts as lost below this height (m)...</summary>
        public const float KnifeLostBelowY = -10f;
        /// <summary>...or when it is gone; after this many seconds lost, a new one appears next to the board.</summary>
        public const float KnifeRespawnSeconds = 5f;

        /// <summary>The progress after one knife stroke (see the table in spec 019).</summary>
        public static CutProgress Stroke(CutProgress progress)
        {
            switch (progress.Shape)
            {
                case FoodLook.ShapeWhole: return new CutProgress(FoodLook.ShapeHalves, 0);
                case FoodLook.ShapeHalves: return new CutProgress(FoodLook.ShapeSlices, 0);
                case FoodLook.ShapeSlices:
                    return progress.Turned != 0 ? new CutProgress(FoodLook.ShapeSticks, 0) : progress;
                case FoodLook.ShapeSticks:
                    return progress.Turned != 0 ? new CutProgress(FoodLook.ShapeCubes, 0) : progress;
                default: return progress; // dice (and anything unknown) stay as they are
            }
        }

        /// <summary>The progress after turning the food 90 degrees: only slices and sticks remember it.</summary>
        public static CutProgress Turn(CutProgress progress)
        {
            if (progress.Shape == FoodLook.ShapeSlices || progress.Shape == FoodLook.ShapeSticks)
                return new CutProgress(progress.Shape, 1);
            return progress;
        }

        /// <summary>The sim cut op's shape choice for a look shape: "halves", "slices", "sticks", "dice"; null for whole.</summary>
        public static string ShapeId(byte shape)
        {
            switch (shape)
            {
                case FoodLook.ShapeHalves: return "halves";
                case FoodLook.ShapeSlices: return "slices";
                case FoodLook.ShapeSticks: return "sticks";
                case FoodLook.ShapeCubes: return "dice";
                default: return null;
            }
        }

        /// <summary>True when a board's knife is missing (despawned, destroyed) or has fallen out of the world.</summary>
        public static bool IsKnifeLost(bool exists, float height)
        {
            return !exists || float.IsNaN(height) || height < KnifeLostBelowY;
        }

        /// <summary>True once the knife has been lost since <paramref name="lostSince"/> for the respawn time.</summary>
        public static bool ShouldRespawnKnife(float lostSince, float now)
        {
            return now - lostSince >= KnifeRespawnSeconds;
        }
    }
}
