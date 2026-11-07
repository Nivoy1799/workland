using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 022 test helper: cooks menu dishes through <see cref="KitchenState"/> and the spec 021 vessel calls, the way
    /// the server does for a player (crate, board, pan or pot on a burner, bowl, pour onto a plate, the plate's dish).
    /// </summary>
    internal sealed class MenuKitchen
    {
        public const int Plate = 70;
        public const int Bowl = 71;
        private const int Pan = HeatTestKit.PanIndex;
        private const int Pot = HeatTestKit.PotIndex;
        private const uint Start = HeatTestKit.StartTick;
        private const double Rate = HeatTestKit.TickRate;

        private int _nextId = 300;

        public KitchenState Kitchen { get; } = KitchenState.CreateDemo();

        /// <summary>Buys an ingredient and cuts it on the board (1 halves, 2 slices, 3 sticks, 4 dice; 0 = whole).</summary>
        public int Buy(string baseId, int cut = 0)
        {
            Assert.AreEqual(CookOutcome.Ok, Kitchen.TryCreateIngredient(baseId, out Item item));
            int id = _nextId++;
            Kitchen.Register(id, item);
            if (cut > 0) HeatTestKit.Cut(Kitchen, id, cut);
            return id;
        }

        /// <summary>Into the pan on a burner that is on (saute: the burner's mode button first), then poured onto the plate.</summary>
        public void PanToPlate(int food, double simSeconds, bool saute = false)
        {
            Kitchen.SetVesselBurner(Pan, HeatTestKit.PanBurner, Start);
            if (saute) Assert.AreEqual(CookOutcome.Ok, Kitchen.TryUseBurner(HeatTestKit.PanBurner, StationRules.PanMode, Start));
            HeatTestKit.SwitchOn(Kitchen, Pan, HeatTestKit.PanBurner);
            Assert.AreEqual(CookOutcome.Ok, Kitchen.TryPutInto(food, Pan, VesselKind.Pan, Start));
            Assert.AreEqual(CookOutcome.Ok, Kitchen.TryPour(Pan, VesselKind.Pan, Plate, VesselKind.Plate,
                Start + HeatTestKit.TicksFor(simSeconds), Rate, out _));
        }

        /// <summary>All foods into the pot on a burner that is on at the same tick, then poured onto the plate.</summary>
        public void PotToPlate(double simSeconds, params int[] foods)
        {
            HeatTestKit.SwitchOn(Kitchen, Pot, HeatTestKit.PotBurner);
            foreach (int food in foods) Assert.AreEqual(CookOutcome.Ok, Kitchen.TryPutInto(food, Pot, VesselKind.Pot, Start));
            Assert.AreEqual(CookOutcome.Ok, Kitchen.TryPour(Pot, VesselKind.Pot, Plate, VesselKind.Plate,
                Start + HeatTestKit.TicksFor(simSeconds), Rate, out _));
        }

        public void OntoPlate(int food)
        {
            Assert.AreEqual(CookOutcome.Ok, Kitchen.TryPutInto(food, Plate, VesselKind.Plate, Start));
        }

        public void IntoBowl(int food)
        {
            Assert.AreEqual(CookOutcome.Ok, Kitchen.TryPutInto(food, Bowl, VesselKind.Bowl, Start));
        }

        public void BowlToPlate()
        {
            Assert.AreEqual(CookOutcome.Ok, Kitchen.TryPour(Bowl, VesselKind.Bowl, Plate, VesselKind.Plate, Start, Rate, out _));
        }

        /// <summary>What the counter sells for the plate (the sim plate op on its parts); the plate is then empty.</summary>
        public Item Served()
        {
            Assert.AreEqual(CookOutcome.Ok, Kitchen.TryPreviewTake(Plate, VesselKind.Plate, out Item dish));
            Kitchen.ClearVessel(Plate);
            return dish;
        }

        /// <summary>Loose food the counter would plate (spec 009 path).</summary>
        public Item PlatedLoose(int food)
        {
            Assert.AreEqual(CookOutcome.Ok, Kitchen.TryPlate(food, out Item plated));
            return plated;
        }

        // ---- the dishes ----

        public static Item ExactFries()
        {
            var k = new MenuKitchen();
            k.PanToPlate(k.Buy("potato", 3), 150);
            return k.Served();
        }

        public static Item ShortFriedSticks()
        {
            var k = new MenuKitchen();
            k.PanToPlate(k.Buy("potato", 3), 40);
            return k.Served();
        }

        public static Item ExactPotatoSoup()
        {
            var k = new MenuKitchen();
            k.PotToPlate(600, k.Buy("potato", 1));
            return k.Served();
        }

        public static Item BoiledWholePotato()
        {
            var k = new MenuKitchen();
            k.PotToPlate(600, k.Buy("potato"));
            return k.Served();
        }

        public static Item RawWholePotato()
        {
            var k = new MenuKitchen();
            return k.PlatedLoose(k.Buy("potato"));
        }

        public static Item ExactOnionSoup()
        {
            var k = new MenuKitchen();
            k.PotToPlate(600, k.Buy("onion"), k.Buy("potato", 1));
            return k.Served();
        }

        public static Item BoiledOnionOnly()
        {
            var k = new MenuKitchen();
            k.PotToPlate(600, k.Buy("onion"));
            return k.Served();
        }

        public static Item ExactTomatoSalad()
        {
            var k = new MenuKitchen();
            k.IntoBowl(k.Buy("tomato", 2));
            k.IntoBowl(k.Buy("onion", 4));
            k.BowlToPlate();
            return k.Served();
        }

        public static Item TomatoAndOnionNotMixed()
        {
            var k = new MenuKitchen();
            k.OntoPlate(k.Buy("tomato", 2));
            k.OntoPlate(k.Buy("onion", 4));
            return k.Served();
        }

        public static Item ExactFriedApple()
        {
            var k = new MenuKitchen();
            k.PanToPlate(k.Buy("apple", 2), 40, saute: true);
            return k.Served();
        }

        public static Item FriedNotSauteedApple()
        {
            var k = new MenuKitchen();
            k.PanToPlate(k.Buy("apple", 2), 40);
            return k.Served();
        }

        public static Item BoiledWholeApple()
        {
            var k = new MenuKitchen();
            k.PotToPlate(600, k.Buy("apple"));
            return k.Served();
        }

        public static Item ExactVeggieMix()
        {
            var k = new MenuKitchen();
            k.OntoPlate(k.Buy("apple", 4));
            k.OntoPlate(k.Buy("tomato", 2));
            k.OntoPlate(k.Buy("onion", 1));
            return k.Served();
        }

        public static Item TwoCutVeggies()
        {
            var k = new MenuKitchen();
            k.OntoPlate(k.Buy("tomato", 2));
            k.OntoPlate(k.Buy("onion", 1));
            return k.Served();
        }
    }
}
