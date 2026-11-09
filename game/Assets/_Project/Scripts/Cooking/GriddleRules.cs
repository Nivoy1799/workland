namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 030, pure: the griddle's controls and a griddle spot's synced bytes, shared by the server (KitchenState) and clients
    /// (prompts, the two-half ring, the flip arc). The griddle station has a power knob (<see cref="PowerOption"/>,
    /// RequestStation) and <see cref="SpotsPerGriddle"/> spots, each a fixed <see cref="VesselKind.Griddle"/> vessel. A spot's
    /// HeatProgress and HeatZone bytes are its DOWN side (the side that cooks now, the 027c pattern); its Mode byte carries which
    /// side is down, a flip counter (mod 4, so every viewer sees each flip) and the UP side's zone with a coarse progress (it
    /// does not change while it is up). No new SyncVar or RPC. Times, temperatures and doneness never leave the server.
    /// </summary>
    public static class GriddleRules
    {
        /// <summary>Spec 030 default to review: six spots on the plate (three across, two deep).</summary>
        public const int SpotsPerGriddle = 6;
        public const int SpotColumns = 3;
        public const int SpotRows = 2;
        /// <summary>The steel plate (m): 0.9 wide, 0.6 deep (spec 030).</summary>
        public const float PlateWidth = 0.9f;
        public const float PlateDepth = 0.6f;
        /// <summary>Station option: the griddle's on/off knob.</summary>
        public const int PowerOption = 1;
        /// <summary>The griddle's StationContents byte: on.</summary>
        public const byte FlagOn = 1;
        /// <summary>Taken off before its longer side had this many sim seconds (1 kitchen second): handed back unchanged.</summary>
        public const float MinCookSimSeconds = 6f;

        private const int DownMask = 0x01;
        private const int FlipShift = 1;
        private const int FlipMask = 0x03;
        private const int UpZoneShift = 3;
        private const int UpZoneMask = 0x07;
        private const int UpFracShift = 6;
        private const int UpFracMask = 0x03;
        private const int FracSteps = 4;

        public static bool IsOn(byte flags) => (flags & FlagOn) != 0;
        public static byte Flags(bool on) => on ? FlagOn : (byte)0;

        /// <summary>
        /// A spot's mode byte: <paramref name="downSide"/> (0 side 1, 1 side 2), the flip counter, and the up side's
        /// <paramref name="up"/> reading (zone and a coarse progress).
        /// </summary>
        public static byte Mode(int downSide, int flips, HeatReading up)
        {
            int zone = (int)up.Zone & UpZoneMask;
            int frac = FractionOf(up.Zone, up.Progress / 255f);
            return (byte)((downSide & DownMask) | ((flips & FlipMask) << FlipShift) | (zone << UpZoneShift) | (frac << UpFracShift));
        }

        /// <summary>0 when side 1 is down, 1 when side 2 is down.</summary>
        public static int DownSideOf(byte mode) => mode & DownMask;

        /// <summary>The flip counter (mod 4).</summary>
        public static int FlipsOf(byte mode) => (mode >> FlipShift) & FlipMask;

        /// <summary>The up side's zone (None for an unknown value).</summary>
        public static HeatZone UpZoneOf(byte mode)
        {
            int z = (mode >> UpZoneShift) & UpZoneMask;
            return z <= (int)HeatZone.Burnt ? (HeatZone)z : HeatZone.None;
        }

        /// <summary>The up side's progress 0..1 as clients know it: the middle of its quarter of its zone's part of the ring.</summary>
        public static float UpProgressOf(byte mode)
        {
            HeatZone zone = UpZoneOf(mode);
            int frac = (mode >> UpFracShift) & UpFracMask;
            Range(zone, out float lo, out float hi);
            if (hi <= lo) return lo;
            return lo + (frac + 0.5f) / FracSteps * (hi - lo);
        }

        /// <summary>The flips that happened between two counter values (0..3; the counter wraps at 4).</summary>
        public static int FlipsBetween(int before, int after) => ((after - before) % 4 + 4) % 4;

        /// <summary>The part of the ring (0..1) each zone covers: Cooking up to the good window, Good, Perfect, then full.</summary>
        private static void Range(HeatZone zone, out float lo, out float hi)
        {
            switch (zone)
            {
                case HeatZone.Cooking: lo = 0f; hi = HeatZoneRules.GoodShare; return;
                case HeatZone.Good: lo = HeatZoneRules.GoodShare; hi = HeatZoneRules.PerfectShare; return;
                case HeatZone.Perfect: lo = HeatZoneRules.PerfectShare; hi = 1f; return;
                case HeatZone.Overdone:
                case HeatZone.Burnt: lo = 1f; hi = 1f; return;
                default: lo = 0f; hi = 0f; return;
            }
        }

        private static int FractionOf(HeatZone zone, float progress01)
        {
            Range(zone, out float lo, out float hi);
            if (hi <= lo) return 0;
            int f = (int)((progress01 - lo) / (hi - lo) * FracSteps);
            return f < 0 ? 0 : (f >= FracSteps ? FracSteps - 1 : f);
        }

        /// <summary>"side 1" or "side 2".</summary>
        public static string SideName(int side) => "side " + (side == 1 ? 2 : 1);

        /// <summary>Prompt words for the knob: "griddle on", "griddle off".</summary>
        public static string PowerText(bool on) => on ? "griddle off" : "griddle on";

        /// <summary>The spot index of a 3 × 2 layout: column (left to right as seen from the aisle) and row (0 front).</summary>
        public static int SpotIndex(int column, int row) => row * SpotColumns + column;
    }
}
