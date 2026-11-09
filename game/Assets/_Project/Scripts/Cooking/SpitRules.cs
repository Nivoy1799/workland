using Worktown.Sim.Cooking;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 036, pure: the seitan spit's controls and synced bytes, shared by the server (SpitDesk, KitchenService) and clients (prompts,
    /// the looks). Requests go through the existing RequestStation (no new RPC):
    /// - <see cref="KnobOption"/>: the heat knob (a StationButton) turns off, low, high, off;
    /// - <see cref="LoadOption"/>: empty hands at the bare spit load a new cone from the shop's stock (one unit of the cone line);
    /// - <see cref="ShaveStartOption"/> on press and <see cref="ShaveStopOption"/> on release of the held knife on the cone (the server
    ///   measures the time between their arrivals in its own ticks; one second is one portion, Sim.Cooking.Spit.PortionsFor).
    /// The cone is the station's slot <see cref="ConeTool"/> (the aim target of the hold, like the sink's basin); the cone itself and the
    /// catch tray are fixed scene vessels (<see cref="VesselKind.SpitCone"/>, <see cref="VesselKind.SpitTray"/>; RequestUse on the tray
    /// scoops one portion into the hands or onto the build in them). Bytes: the station's StationContents byte (<see cref="Flags"/>: the
    /// heat level, someone shaving, a cone loaded); the cone vessel's Mode (<see cref="ConeMode"/>: portions left and a shave counter, so
    /// every viewer sees each shave) and its HeatProgress / HeatZone (the outer layer, the 027c pattern: the ring, the cues, the sizzle);
    /// the tray vessel's names and looks (its portions). Times, temperatures and doneness never leave the server.
    /// </summary>
    public static class SpitRules
    {
        /// <summary>The cone: the station's one slot (the knife is held on it to shave; empty hands load a cone there).</summary>
        public const int ConeTool = 0;
        public const int KnobOption = 1;
        public const int LoadOption = 2;
        public const int ShaveStartOption = 3;
        public const int ShaveStopOption = 4;

        /// <summary>Spec 036: the catch tray holds this many portions (it must be scooped before more is shaved).</summary>
        public const int TrayCapacity = Spit.MaxPortionsPerShave;

        private const int LevelMask = 0x03;
        public const int ShavingBit = 1 << 2;
        public const int ConeBit = 1 << 3;
        private const int PortionMask = 0x3F;
        private const int ShaveShift = 6;
        private const int ShaveMask = 0x03;

        /// <summary>Whether option <paramref name="option"/> needs the player in reach: everything but the shave's release (the press was in reach).</summary>
        public static bool NeedsReach(StationKind kind, int option) => !(kind == StationKind.Spit && option == ShaveStopOption);

        public static bool IsShaveOption(int option) => option == ShaveStartOption || option == ShaveStopOption;

        // ---- the bytes ----

        /// <summary>The station's byte: the heat level (bits 0-1), someone shaving (bit 2), a cone with seitan on it (bit 3).</summary>
        public static byte Flags(int level, bool shaving, bool cone) =>
            (byte)((level < 0 ? 0 : level > Spit.High ? Spit.High : level) | (shaving ? ShavingBit : 0) | (cone ? ConeBit : 0));

        public static int LevelOf(byte flags) => flags & LevelMask;
        public static bool IsShaving(byte flags) => (flags & ShavingBit) != 0;
        public static bool HasCone(byte flags) => (flags & ConeBit) != 0;
        public static bool IsOn(byte flags) => LevelOf(flags) != Spit.Off;
        public static byte WithShaving(byte flags, bool shaving) => (byte)(shaving ? flags | ShavingBit : flags & ~ShavingBit);

        /// <summary>The cone vessel's mode byte: the portions left (bits 0-5) and a shave counter (bits 6-7, mod 4).</summary>
        public static byte ConeMode(int portions, int shaves) =>
            (byte)((portions < 0 ? 0 : portions > PortionMask ? PortionMask : portions) | ((shaves & ShaveMask) << ShaveShift));

        public static int PortionsOf(byte mode) => mode & PortionMask;
        public static int ShavesOf(byte mode) => (mode >> ShaveShift) & ShaveMask;

        /// <summary>The cone's share left (0..1) for the shrinking look.</summary>
        public static float ShareLeft(byte mode) => PortionsOf(mode) / (float)Spit.PortionsPerCone;

        // ---- what a client may offer (the server checks again) ----

        /// <summary>
        /// The knife on the cone: a cone with seitan (NoCone), the knife in the hands (NoKnife), room in the tray (TrayFull), nobody else
        /// shaving (DispenserBusy: the bit, not our own).
        /// </summary>
        public static CookOutcome CanShave(bool cone, bool holdsKnife, int tray, bool busy)
        {
            if (!cone) return CookOutcome.NoCone;
            if (!holdsKnife) return CookOutcome.NoKnife;
            if (tray >= TrayCapacity) return CookOutcome.TrayFull;
            return busy ? CookOutcome.DispenserBusy : CookOutcome.Ok;
        }

        /// <summary>Empty hands at a spit without a cone (or one that ran out) load a new one; the stock is checked by the caller.</summary>
        public static CookOutcome CanLoad(bool holding, bool cone, bool shaving)
        {
            if (cone) return CookOutcome.ConeLoaded;
            if (holding) return CookOutcome.HandsFull;
            return shaving ? CookOutcome.DispenserBusy : CookOutcome.Ok;
        }

        /// <summary>The tray gives empty hands one portion, or puts one on an open build in the hands.</summary>
        public static CookOutcome CanScoop(int tray, bool holding, bool openBuild)
        {
            if (tray <= 0) return CookOutcome.TrayEmpty;
            if (holding && !openBuild) return CookOutcome.HandsFull;
            return CookOutcome.Ok;
        }

        public static string LevelName(int level) => level == Spit.High ? "high" : level == Spit.Low ? "low" : "off";

        /// <summary>"Click · heat low" (the knob's next level).</summary>
        public static string KnobText(byte flags) => "heat " + LevelName(Spit.NextLevel(LevelOf(flags)));
    }
}
