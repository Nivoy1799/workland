using UnityEngine;
using Worktown.Game.Art;

namespace Worktown.Game.Npc
{
    /// <summary>
    /// Spec 032, pure: the guest's hand-off. Which parcel it carries away (a wrap in its paper, the flatbread in a bag, the veggie box
    /// in its box, anything else in a takeaway bag) and the arc from the counter into its hand: <see cref="FlySeconds"/> with an
    /// ease-out and a small lift (feel.md 2: nothing teleports).
    /// </summary>
    public static class HandoffRules
    {
        public const float FlySeconds = 0.35f;
        public const float Lift = 0.12f;

        /// <summary>
        /// Spec 035: an eat-in guest takes a plate dish on its plate (it comes back dirty later); a build is held in its paper, box or
        /// bag either way, and a take-away guest gets its parcel.
        /// </summary>
        public static ArtKey ParcelOf(byte dish, bool eatIn) => eatIn && IsPlateDish(dish) ? ArtKey.FoodPlate : ParcelOf(dish);

        /// <summary>
        /// Spec 044 (035 feels off: "eat-in guests leave holding their plate"): an eat-in guest with a plate dish that has paid (its mood
        /// leaves <see cref="CustomerRules.MoodPaying"/> for a leaving mood) puts the plate on the dish return before it walks out (there are
        /// no tables yet; the plate comes back dirty through the dish return). Take-away parcels and builds stay in the hand.
        /// </summary>
        public static bool LeavesPlate(byte previousMood, byte mood, bool eatIn, byte dish) =>
            previousMood == CustomerRules.MoodPaying && mood != CustomerRules.MoodPaying && mood != CustomerRules.MoodWaiting
            && eatIn && IsPlateDish(dish);

        /// <summary>Spec 035: the dishes served on a plate (everything but the assembly board's builds).</summary>
        public static bool IsPlateDish(byte dish)
        {
            switch (dish)
            {
                case Menu.SeitanWrap:
                case Menu.FalafelWrap:
                case Menu.SeitanFlatbread:
                case Menu.VeggieBox:
                case Menu.None:
                    return false;
                default:
                    return true;
            }
        }

        public static ArtKey ParcelOf(byte dish)
        {
            switch (dish)
            {
                case Menu.SeitanWrap:
                case Menu.FalafelWrap:
                    return ArtKey.FoodWrapPaper;
                case Menu.VeggieBox:
                    return ArtKey.PropTakeawayBox;
                default:
                    return ArtKey.PropPaperBag;
            }
        }

        /// <summary>The point at share <paramref name="t"/> (0..1) of the arc: ease-out along the line, plus a lift that peaks halfway.</summary>
        public static Vector3 Arc(Vector3 from, Vector3 to, float t)
        {
            t = Mathf.Clamp01(t);
            float e = 1f - (1f - t) * (1f - t);
            return Vector3.Lerp(from, to, e) + Vector3.up * (Lift * 4f * t * (1f - t));
        }
    }
}
