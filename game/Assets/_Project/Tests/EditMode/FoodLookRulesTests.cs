using System;
using System.Collections.Generic;
using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 017 AC2: the coarse look read off sim items.</summary>
    public class FoodLookRulesTests
    {
        private const int Table = 10;
        private const int Bowl = 11;
        private const byte PotatoIndex = 0;
        private const byte TomatoIndex = 2;
        private const byte OnionIndex = 3;
        private int _nextId = 100;

        private static readonly IReadOnlyDictionary<string, ParamValue> NoParams = new Dictionary<string, ParamValue>();

        private int Food(KitchenState kitchen, string baseId, params int[] boardOptions)
        {
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateIngredient(baseId, out Item item));
            int id = _nextId++;
            kitchen.Register(id, item);
            foreach (int option in boardOptions)
                Assert.AreEqual(CookOutcome.Ok, kitchen.TryApply(id, StationKind.Board, option, out _, out _));
            return id;
        }

        private static Item ItemOf(KitchenState kitchen, int id)
        {
            Assert.IsTrue(kitchen.TryGet(id, out Item item));
            return item;
        }

        private static void AssertLook(FoodLook look, byte ingredient, byte shape, byte doneness, byte kind, byte parts)
        {
            Assert.AreEqual(ingredient, look.Ingredient, "ingredient");
            Assert.AreEqual(shape, look.Shape, "shape");
            Assert.AreEqual(doneness, look.Doneness, "doneness");
            Assert.AreEqual(kind, look.Kind, "kind");
            Assert.AreEqual(parts, look.PartCount, "part count");
        }

        [Test]
        public void RawPotato_IsWholeRawSingle()
        {
            var kitchen = KitchenState.CreateDemo();
            FoodLook look = FoodLookRules.From(ItemOf(kitchen, Food(kitchen, "potato")));
            AssertLook(look, PotatoIndex, FoodLook.ShapeWhole, FoodLook.DonenessRaw, FoodLook.KindSingle, 0);
            Assert.AreEqual(FoodLook.HeatNone, look.Heat);
            Assert.IsEmpty(FoodLookRules.PartsOf(ItemOf(kitchen, _nextId - 1)));
        }

        [Test]
        public void DemoIngredients_MapToTheirCrateIndex()
        {
            var kitchen = KitchenState.CreateDemo();
            for (int i = 0; i < DemoIngredients.All.Count; i++)
                Assert.AreEqual(i, FoodLookRules.From(ItemOf(kitchen, Food(kitchen, DemoIngredients.All[i].Id))).Ingredient);
        }

        [TestCase(1, FoodLook.ShapeHalves)]
        [TestCase(2, FoodLook.ShapeSlices)]
        [TestCase(3, FoodLook.ShapeSticks)]
        [TestCase(4, FoodLook.ShapeCubes)]
        public void BoardOption_GivesShape(int option, byte shape)
        {
            var kitchen = KitchenState.CreateDemo();
            FoodLook look = FoodLookRules.From(ItemOf(kitchen, Food(kitchen, "potato", option)));
            AssertLook(look, PotatoIndex, shape, FoodLook.DonenessRaw, FoodLook.KindSingle, 0);
        }

        [Test]
        public void LastCut_GivesTheShape()
        {
            var kitchen = KitchenState.CreateDemo();
            // Halves, then cubes: the last (finer) cut wins.
            Assert.AreEqual(FoodLook.ShapeCubes, FoodLookRules.From(ItemOf(kitchen, Food(kitchen, "onion", 1, 4))).Shape);
        }

        [Test]
        public void SticksFried150_AreSticksMedium()
        {
            var kitchen = KitchenState.CreateDemo();
            int id = Food(kitchen, "potato", 3);
            FoodLook look = FoodLookRules.From(HeatTestKit.Fry(kitchen, id, 150));
            AssertLook(look, PotatoIndex, FoodLook.ShapeSticks, FoodLook.DonenessMedium, FoodLook.KindSingle, 0);
            Assert.AreEqual(FoodLook.HeatFried, look.Heat);
        }

        [Test]
        public void Boiled30_IsShort()
        {
            var kitchen = KitchenState.CreateDemo();
            int id = Food(kitchen, "potato");
            FoodLook look = FoodLookRules.From(HeatTestKit.Boil(kitchen, id, 30));
            AssertLook(look, PotatoIndex, FoodLook.ShapeWhole, FoodLook.DonenessShort, FoodLook.KindSingle, 0);
            Assert.AreEqual(FoodLook.HeatBoiled, look.Heat);
        }

        [Test]
        public void FriedLong_IsLong()
        {
            var kitchen = KitchenState.CreateDemo();
            int id = Food(kitchen, "potato", 3);
            Assert.AreEqual(FoodLook.DonenessLong, FoodLookRules.From(HeatTestKit.Fry(kitchen, id, 400)).Doneness);
        }

        [Test]
        public void PlateOfFriesAndTomatoSlices_IsPlatedWithTwoPartsInInputOrder()
        {
            var kitchen = KitchenState.CreateDemo();
            int fries = Food(kitchen, "potato", 3);
            HeatTestKit.Fry(kitchen, fries, 150);
            int tomato = Food(kitchen, "tomato", 2);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryAddToStation(fries, Table, StationKind.PlatingTable));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryAddToStation(tomato, Table, StationKind.PlatingTable));

            var friesLook = new FoodLook(PotatoIndex, FoodLook.ShapeSticks, FoodLook.DonenessMedium, FoodLook.HeatFried, 0, 0);
            var tomatoLook = new FoodLook(TomatoIndex, FoodLook.ShapeSlices, FoodLook.DonenessRaw, FoodLook.HeatNone, 0, 0);

            // On the table: the order added.
            IReadOnlyList<FoodLook> onTable = kitchen.LooksOnStation(Table);
            Assert.AreEqual(2, onTable.Count);
            Assert.AreEqual(friesLook, onTable[0]);
            Assert.AreEqual(tomatoLook, onTable[1]);

            Assert.AreEqual(CookOutcome.Ok, kitchen.TryTakeFromStation(Table, StationKind.PlatingTable, out Item plate));
            FoodLook look = FoodLookRules.From(plate);
            Assert.AreEqual(FoodLook.KindPlated, look.Kind);
            Assert.AreEqual(2, look.PartCount);

            // On the plate: the sim's canonical input order (plate is commutative, sorted by item hash),
            // which puts the tomato slices first.
            IReadOnlyList<FoodLook> parts = FoodLookRules.PartsOf(plate);
            Assert.AreEqual(2, parts.Count);
            Assert.AreEqual(tomatoLook, parts[0], "part 0");
            Assert.AreEqual(friesLook, parts[1], "part 1");
            Assert.IsEmpty(kitchen.LooksOnStation(Table), "taking the plate empties the table");
        }

        [Test]
        public void MixOfThree_IsMixedWithThreeParts()
        {
            var kitchen = KitchenState.CreateDemo();
            foreach (string id in new[] { "onion", "tomato", "apple" })
                Assert.AreEqual(CookOutcome.Ok, kitchen.TryAddToStation(Food(kitchen, id, 4), Bowl, StationKind.Bowl));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryTakeFromStation(Bowl, StationKind.Bowl, out Item mix));

            FoodLook look = FoodLookRules.From(mix);
            Assert.AreEqual(FoodLook.KindMixed, look.Kind);
            Assert.AreEqual(3, look.PartCount);
            Assert.AreEqual(FoodLook.ShapeCubes, look.Shape, "a mix takes its first part's shape, never whole");
            IReadOnlyList<FoodLook> parts = FoodLookRules.PartsOf(mix);
            Assert.AreEqual(3, parts.Count);
            foreach (FoodLook part in parts)
            {
                Assert.AreEqual(FoodLook.ShapeCubes, part.Shape);
                Assert.AreEqual(FoodLook.DonenessRaw, part.Doneness);
                Assert.AreEqual(FoodLook.HeatNone, part.Heat);
            }
        }

        [Test]
        public void MixOfFive_ShowsAtMostFourParts()
        {
            var factory = new ItemFactory(new ItemCatalog(DemoIngredients.All), OperationCatalog.CreateDefault());
            var inputs = new List<Item>();
            foreach (string id in new[] { "potato", "apple", "tomato", "onion", "potato" })
                inputs.Add(factory.CreateBase(id).Item);
            Item mix = factory.Apply("mix", inputs, NoParams).Item;
            Assert.IsNotNull(mix);
            Assert.AreEqual(FoodLook.MaxParts, FoodLookRules.From(mix).PartCount);
            Assert.AreEqual(FoodLook.MaxParts, FoodLookRules.PartsOf(mix).Count);
        }

        /// <summary>Potato cubes and onion cubes mixed in the bowl, then fried 150 sim s (medium).</summary>
        private Item FriedCubeMix(KitchenState kitchen)
        {
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryAddToStation(Food(kitchen, "potato", 4), Bowl, StationKind.Bowl));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryAddToStation(Food(kitchen, "onion", 4), Bowl, StationKind.Bowl));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryTakeFromStation(Bowl, StationKind.Bowl, out Item mix));
            int id = _nextId++;
            kitchen.Register(id, mix);
            return HeatTestKit.Fry(kitchen, id, 150);
        }

        [Test]
        public void FriedMix_IsMixedAndFried_AndItsPartsShowFried()
        {
            var kitchen = KitchenState.CreateDemo();
            Item fried = FriedCubeMix(kitchen);
            FoodLook look = FoodLookRules.From(fried);
            Assert.AreEqual(FoodLook.KindMixed, look.Kind);
            Assert.AreEqual(2, look.PartCount);
            Assert.AreEqual(FoodLook.ShapeCubes, look.Shape, "drawn as cubes, not as a whole ingredient");
            Assert.AreEqual(FoodLook.DonenessMedium, look.Doneness);
            Assert.AreEqual(FoodLook.HeatFried, look.Heat);

            IReadOnlyList<FoodLook> parts = FoodLookRules.PartsOf(fried);
            Assert.AreEqual(2, parts.Count);
            foreach (FoodLook part in parts)
            {
                Assert.AreEqual(FoodLook.ShapeCubes, part.Shape);
                Assert.AreEqual(FoodLook.DonenessMedium, part.Doneness, "the mix's heat is carried onto its parts");
                Assert.AreEqual(FoodLook.HeatFried, part.Heat);
                Assert.AreEqual(FoodLook.KindSingle, part.Kind);
            }
        }

        [Test]
        public void FriedMixOnAPlate_ShowsAFriedCubePartAndARawTomato()
        {
            var kitchen = KitchenState.CreateDemo();
            Item fried = FriedCubeMix(kitchen);
            int mixId = _nextId++;
            kitchen.Register(mixId, fried);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryAddToStation(mixId, Table, StationKind.PlatingTable));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryAddToStation(Food(kitchen, "tomato", 2), Table, StationKind.PlatingTable));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryTakeFromStation(Table, StationKind.PlatingTable, out Item plate));

            FoodLook look = FoodLookRules.From(plate);
            Assert.AreEqual(FoodLook.KindPlated, look.Kind);
            Assert.AreEqual(2, look.PartCount);

            // Canonical order (sorted by item hash): the tomato slices, then the fried mix.
            IReadOnlyList<FoodLook> parts = FoodLookRules.PartsOf(plate);
            Assert.AreEqual(2, parts.Count);
            Assert.AreEqual(new FoodLook(TomatoIndex, FoodLook.ShapeSlices, FoodLook.DonenessRaw, FoodLook.HeatNone, 0, 0), parts[0],
                "the plate itself is not heated, so the tomato stays raw");
            // A mix's ingredient is its first sim component (sorted by base id): onion before potato.
            Assert.AreEqual(new FoodLook(OnionIndex, FoodLook.ShapeCubes, FoodLook.DonenessMedium, FoodLook.HeatFried,
                FoodLook.KindMixed, 2), parts[1], "the fried mix: cubes, medium, fried");
        }

        [Test]
        public void UnknownBaseId_GivesIngredient255()
        {
            var dragonfruit = new BaseItemDef("dragonfruit", "dragonfruit",
                new ItemStats(0.5f, 0f, 0.2f, 0f, 0f, 0f, 0.3f, 0.5f, 0f, 0.8f),
                new DonenessWindow(0f, 1f), Array.Empty<string>());
            var factory = new ItemFactory(new ItemCatalog(new[] { dragonfruit }), OperationCatalog.CreateDefault());
            Item item = factory.CreateBase("dragonfruit").Item;
            Assert.AreEqual(FoodLook.UnknownIngredient, FoodLookRules.From(item).Ingredient);
            Assert.AreEqual(FoodLook.UnknownIngredient, FoodLookRules.From(null).Ingredient);
            Assert.AreEqual(FoodLook.UnknownIngredient, FoodLookRules.IngredientIndex(null));
        }

        [Test]
        public void BucketIds_MapToCoarseValues()
        {
            Assert.AreEqual(FoodLook.ShapeWhole, FoodLookRules.ShapeOf("unknown"));
            Assert.AreEqual(FoodLook.ShapeCubes, FoodLookRules.ShapeOf("dice"));
            Assert.AreEqual(FoodLook.DonenessRaw, FoodLookRules.DonenessOf(string.Empty));
            Assert.AreEqual(FoodLook.DonenessLong, FoodLookRules.DonenessOf("long"));
        }

        [Test]
        public void HeatLook_FollowsTheLabelBucketWhileCooking()
        {
            var kitchen = KitchenState.CreateDemo();
            int id = Food(kitchen, "potato", 3);
            Assert.IsNull(kitchen.HeatLook(HeatTestKit.PanIndex, HeatTestKit.StartTick, HeatTestKit.TickRate));
            HeatTestKit.SwitchOn(kitchen, HeatTestKit.PanIndex, StationKind.Pan);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutOnHeat(id, HeatTestKit.PanIndex, StationKind.Pan, HeatTestKit.StartTick));

            FoodLook? raw = kitchen.HeatLook(HeatTestKit.PanIndex, HeatTestKit.StartTick, HeatTestKit.TickRate);
            Assert.IsTrue(raw.HasValue);
            AssertLook(raw.Value, PotatoIndex, FoodLook.ShapeSticks, FoodLook.DonenessRaw, FoodLook.KindSingle, 0);
            Assert.AreEqual(FoodLook.HeatNone, raw.Value.Heat);

            FoodLook? medium = kitchen.HeatLook(HeatTestKit.PanIndex,
                HeatTestKit.StartTick + HeatTestKit.TicksFor(150), HeatTestKit.TickRate);
            Assert.AreEqual(FoodLook.DonenessMedium, medium.Value.Doneness);
            Assert.AreEqual(FoodLook.HeatFried, medium.Value.Heat);
            Assert.AreEqual(FoodLook.ShapeSticks, medium.Value.Shape);
        }
    }
}
