using Worktown.Game.Player;

namespace Worktown.Game.Art
{
    /// <summary>Spec 044: the pure parts of <see cref="ExitFlightLook"/>: who draws the flight, how long it takes and its size on the way.</summary>
    public static class ExitFlightRules
    {
        /// <summary>The size at the end of the flight (it goes into the pot, the can or the slot).</summary>
        public const float EndSize = 0.7f;

        /// <summary>
        /// A despawned item flies to its destination for this viewer when the server named one (<paramref name="hasExit"/>), the model was
        /// drawn at all (<paramref name="hasDrawn"/>, never on a headless server) and this viewer did not draw it going there already: its
        /// own put-into (<paramref name="predicted"/>) is drawn at the destination since the click.
        /// </summary>
        public static bool ShowsFlight(bool hasExit, bool hasDrawn, bool predicted) => hasExit && hasDrawn && !predicted;

        /// <summary>s: a toss takes a little longer than a pick-up (1.6 x FeelSettings.PickupSeconds, 0.24 s by default), 0.2 to 0.5 s.</summary>
        public static float Seconds(FeelSettings feel)
        {
            float s = (feel != null ? feel.PickupSeconds : 0.15f) * 1.6f;
            return float.IsNaN(s) || s < 0.2f ? 0.2f : s > 0.5f ? 0.5f : s;
        }

        /// <summary>m: the arc's lift, twice a pick-up's (it is tossed in, not lifted).</summary>
        public static float Lift(FeelSettings feel) => (feel != null ? feel.PickupLift : 0.08f) * 2f;

        /// <summary>The copy's size at <paramref name="t"/> (0..1): full size for the first half, then down to <see cref="EndSize"/>.</summary>
        public static float Size(float t)
        {
            float u = FeelRules.Clamp01((t - 0.5f) * 2f);
            return 1f + (EndSize - 1f) * u;
        }
    }
}
