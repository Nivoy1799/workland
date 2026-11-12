using Worktown.Game.Cooking;
using Worktown.Game.Shop;

namespace Worktown.Game.Climb
{
    /// <summary>
    /// Spec 053, pure: what is open at each stage of the D34 climb. <see cref="OpenKitchen"/> (0) is the dev and test kitchen (every
    /// station: <c>-host</c>, <c>-server</c>, the tours); <see cref="FoodCorner"/> (1) is stage 1: the dry shelf's potato slot, the fryer,
    /// the plate shelf, the serving counter, the register's cash tray, the menu board (spec 037: the fries' price) and the trash (the can and the bin outside: the hygiene score needs
    /// them). Spec 057: <see cref="FirstRestaurant"/> (2) opens everything; the goal board is open at every stage (its own rules refuse). The cutting board is carried cookware, not a station, and stays open. Everything else is closed: the stoves, the griddle,
    /// the spit, the assembly board, the drink dispenser, the fridge, the ordering terminal, the dish return, the sink, the card terminal
    /// and every other shelf slot. The server refuses a closed station or slot with <see cref="CookOutcome.NotOpenYet"/>.
    /// </summary>
    public static class ShopStageRules
    {
        public const byte OpenKitchen = 0;
        public const byte FoodCorner = 1;
        /// <summary>Spec 057: stage 2, the first restaurant: every station is open (the menu limits what guests order, not what the crew cooks).</summary>
        public const byte FirstRestaurant = 2;
        /// <summary>The highest stage this build has (a save's stage is clamped to it).</summary>
        public const byte HighestStage = FirstRestaurant;

        /// <summary>The reason shown for a closed station, on the server's refusal and in the client's prompt.</summary>
        public const string NotOpenText = "Opens with your restaurant";

        /// <summary>Whether slot (station option) <paramref name="slot"/> of a station of <paramref name="kind"/> is open at <paramref name="stage"/>.</summary>
        public static bool IsOpen(byte stage, StationKind kind, int slot)
        {
            if (stage == OpenKitchen || stage >= FirstRestaurant) return true;
            if (kind == StationKind.GoalBoard) return true; // spec 057: the board refuses by its own rules (GoalBoardRules)
            switch (kind)
            {
                case StationKind.Shelf:
                {
                    int line = StockCatalog.LineOfSlot(kind, slot);
                    return line >= 0 && StockCatalog.Goods[line].Id == FoodCornerRules.StockGood;
                }
                case StationKind.Register: return slot != RegisterRules.CardOption;
                case StationKind.Fryer:
                case StationKind.MenuBoard: // spec 037: the fries' price
                case StationKind.PlateShelf:
                case StationKind.Counter:
                case StationKind.TrashCan:
                case StationKind.Bin:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Whether a station of <paramref name="kind"/> has anything open at <paramref name="stage"/> (it gets no cover).</summary>
        public static bool IsStationOpen(byte stage, StationKind kind)
        {
            if (stage == OpenKitchen || stage >= FirstRestaurant) return true;
            return kind == StationKind.Shelf || kind == StationKind.Register || IsOpen(stage, kind, 0);
        }

        /// <summary>
        /// Whether cookware of <paramref name="kind"/> may be used at <paramref name="stage"/>: the fixed parts of a closed station (the
        /// griddle's spots, the assembly board's build spot, the spit's cone and tray) are closed with it. Carried cookware stays open
        /// (the board, plates, the bowl; a pan or pot without a burner cooks nothing).
        /// </summary>
        public static bool IsOpenVessel(byte stage, VesselKind kind)
        {
            if (stage == OpenKitchen || stage >= FirstRestaurant) return true;
            return kind != VesselKind.Griddle && kind != VesselKind.Assembly && kind != VesselKind.SpitCone && kind != VesselKind.SpitTray;
        }

        /// <summary>A save's stage for this build: 1 up to <see cref="HighestStage"/> (an unknown or higher stage loads as the highest).</summary>
        public static byte ClampSaved(int stage) => (byte)(stage < FoodCorner ? FoodCorner : stage > HighestStage ? HighestStage : stage);
    }
}
