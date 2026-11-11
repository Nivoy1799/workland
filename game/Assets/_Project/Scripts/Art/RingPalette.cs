using UnityEngine;
using Worktown.Game.Cooking;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 048: the colours of the heat ring (HeatRingRules) and the guests' patience ring (PatienceRingRules). <see cref="Standard"/>
    /// is the spec 027c / 032 set (Sage good, Mustard perfect, Terracotta overdone, Brick burnt); <see cref="ColourBlind"/> is the
    /// settings' alternative (Palette.Safe*: sky blue good, yellow perfect, orange overdone, vermillion burnt), so good and overdone are
    /// never told apart by red against green alone. <see cref="Current"/> is set by Settings.GameSettings.Use; the ring views rebuild
    /// their meshes when <see cref="Version"/> changes.
    /// </summary>
    public sealed class RingPalette
    {
        public static readonly RingPalette Standard = new RingPalette(Palette.Cream, Palette.Sage, Palette.Mustard, Palette.Terracotta,
            Palette.Brick);
        public static readonly RingPalette ColourBlind = new RingPalette(Palette.Cream, Palette.SafeSkyBlue, Palette.SafeYellow,
            Palette.SafeOrange, Palette.SafeVermillion);

        private static RingPalette _current = Standard;

        private RingPalette(Color cooking, Color good, Color perfect, Color overdone, Color burnt)
        {
            Cooking = cooking;
            Good = good;
            Perfect = perfect;
            Overdone = overdone;
            Burnt = burnt;
        }

        public Color Cooking { get; }
        public Color Good { get; }
        public Color Perfect { get; }
        public Color Overdone { get; }
        public Color Burnt { get; }

        /// <summary>The set the rings draw with now.</summary>
        public static RingPalette Current => _current;
        /// <summary>Bumped when <see cref="Current"/> changes.</summary>
        public static int Version { get; private set; }

        /// <summary>Chooses the colour-blind friendly set (true) or the standard one.</summary>
        public static void Use(bool colourBlind)
        {
            RingPalette wanted = colourBlind ? ColourBlind : Standard;
            if (wanted == _current) return;
            _current = wanted;
            Version++;
        }

        /// <summary>A heat zone's fill colour (Cooking for Raw and anything unknown).</summary>
        public Color Fill(HeatZone zone)
        {
            switch (zone)
            {
                case HeatZone.Good: return Good;
                case HeatZone.Perfect: return Perfect;
                case HeatZone.Overdone: return Overdone;
                case HeatZone.Burnt: return Burnt;
                default: return Cooking;
            }
        }

        /// <summary>The patience ring's fill: good while more than half is left, perfect's colour down to the low share, then overdone's.</summary>
        public Color Patience(bool low, float shareLeft) => low ? Overdone : shareLeft > 0.5f ? Good : Perfect;
    }
}
