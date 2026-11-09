using UnityEngine;
using Worktown.Game.Cooking;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 029, pure: how the fryer looks from what every client knows (the fryer's flags, a basket's mode byte, contents
    /// and heat zone): the basket's pose between the oil and the hook, its shake, and how hard the oil bubbles.
    /// </summary>
    public static class FryerArtRules
    {
        /// <summary>The lower and raise arc (s).</summary>
        public const float ArcSeconds = 0.35f;
        /// <summary>Extra lift in the middle of the arc (m), so it reads as a swing, not a slide.</summary>
        public const float ArcLift = 0.035f;
        /// <summary>The shake: a few quick jolts front and back (s, m, jolts).</summary>
        public const float ShakeSeconds = 0.32f;
        public const float ShakeAmplitude = 0.025f;
        public const int ShakeJolts = 3;
        /// <summary>Drops per second while a raised basket drains.</summary>
        public const float DripRate = 20f;

        /// <summary>
        /// Bubbles per second in one basket's well: none while the fryer is off; a light shimmer of hot oil without food in it;
        /// with food lowered, a strong boil at first (the water leaves) that calms as it gets done, per portion.
        /// </summary>
        public static float BubbleRate(bool fryerOn, BasketState state, int load, HeatZone zone)
        {
            if (!fryerOn) return 0f;
            if (state != BasketState.Lowered || load <= 0) return 4f;
            float perPortion;
            switch (zone)
            {
                case HeatZone.None:
                case HeatZone.Raw: perPortion = 34f; break;
                case HeatZone.Cooking: perPortion = 42f; break;
                case HeatZone.Good: perPortion = 34f; break;
                case HeatZone.Perfect: perPortion = 28f; break;
                case HeatZone.Overdone: perPortion = 20f; break;
                default: perPortion = 12f; break;
            }
            return perPortion * Mathf.Min(load, 2);
        }

        /// <summary>The pose (0 lowered into the oil, 1 raised on the hook) <paramref name="t"/> s into an arc from
        /// <paramref name="from"/> towards <paramref name="lowered"/>: smooth start and stop.</summary>
        public static float Pose(float from, bool lowered, float t)
        {
            float to = lowered ? 0f : 1f;
            float u = ArcSeconds > 0f ? Mathf.Clamp01(t / ArcSeconds) : 1f;
            return Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, u));
        }

        /// <summary>The extra arc lift <paramref name="t"/> s into a move (0 at both ends).</summary>
        public static float Lift(float t)
        {
            float u = ArcSeconds > 0f ? Mathf.Clamp01(t / ArcSeconds) : 1f;
            return ArcLift * Mathf.Sin(Mathf.PI * u);
        }

        /// <summary>The shake's front-back offset (m) <paramref name="t"/> s after it started; 0 outside it.</summary>
        public static float ShakeOffset(float t)
        {
            if (!(t >= 0f) || t >= ShakeSeconds) return 0f;
            float u = t / ShakeSeconds;
            return ShakeAmplitude * (1f - u) * Mathf.Sin(2f * Mathf.PI * ShakeJolts * u);
        }

        /// <summary>How many shakes happened between two synced shake counters (mod 4).</summary>
        public static int ShakesBetween(int before, int after) => (after - before) & 3;
    }
}
