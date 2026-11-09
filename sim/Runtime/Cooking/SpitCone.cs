using System;

namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 036, immutable: one seitan cone on the spit: the portions left, the heat time its outer layer has had (sim seconds at
    /// high heat; the doneness of a shave now comes from it through the <c>shave</c> op) and how many shaves it has had. The clock comes
    /// from the caller (the server's ticks), never from the system.
    /// </summary>
    public sealed record SpitCone(int Portions, float OuterSeconds, int Shaves)
    {
        /// <summary>A fresh cone: <see cref="Spit.PortionsPerCone"/> portions, a raw outer layer.</summary>
        public static SpitCone Fresh() => new SpitCone(Spit.PortionsPerCone, 0f, 0);

        /// <summary>The cone ran out (load a new one).</summary>
        public bool IsEmpty => Portions <= 0;

        /// <summary>
        /// The cone after <paramref name="simSeconds"/> in front of the panel at heat level <paramref name="level"/>: the outer layer
        /// gains that time times the level's rate (<see cref="Spit.RateOf"/>), at most <see cref="Spit.MaxLayerSeconds"/>. An empty cone
        /// does not cook.
        /// </summary>
        public SpitCone Heat(float simSeconds, int level)
        {
            if (IsEmpty || float.IsNaN(simSeconds) || simSeconds <= 0f) return this;
            float rate = Spit.RateOf(level);
            if (rate <= 0f) return this;
            return this with { OuterSeconds = Math.Min(Spit.MaxLayerSeconds, OuterSeconds + simSeconds * rate) };
        }

        /// <summary>
        /// A shave held <paramref name="holdSeconds"/> (server-measured) with room for <paramref name="room"/> portions in the tray:
        /// <paramref name="portions"/> = <see cref="Spit.PortionsFor"/> capped by the room and what is left. Each has the outer layer's
        /// heat time (<see cref="OuterSeconds"/> of this cone, before the shave). After it the new outer layer starts at
        /// <see cref="Spit.ResetSeconds"/>, or stays where it was when that was less cooked. No portion: this cone unchanged.
        /// </summary>
        public SpitCone Shave(double holdSeconds, int room, out int portions)
        {
            portions = Math.Min(Spit.PortionsFor(holdSeconds), Math.Min(Math.Max(0, room), Math.Max(0, Portions)));
            if (portions <= 0)
            {
                portions = 0;
                return this;
            }
            return new SpitCone(Portions - portions, Math.Min(OuterSeconds, Spit.ResetSeconds), Shaves + 1);
        }
    }
}
